using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class CloakTransitionTests
{
    [Fact]
    public void OwningClientStartsFadeAndSoundBeforeTheServerReply()
    {
        var (scene, pawn, net) = Client();

        SpyCloak.SetActive(pawn, true);
        Assert.Equal(255, pawn.DesiredGlassEffect);
        Assert.Equal(255f / pawn.PerturbationDefaultValue, pawn.SlopeGlassEffect);
        Assert.Equal(1, TransitionSounds(scene));
        var request = Assert.Single(net.ToServer, c => c.Call.Function == new Name("SetDesiredGlassValue")).Call;
        Assert.NotEqual(0, request.Int2);

        PawnVision.TickGlass(pawn);
        Assert.InRange(pawn.GlassEffect, (byte)1, (byte)254);

        float slope = pawn.SlopeGlassEffect;
        float rampBase = pawn.BGlassEffect;
        byte amount = pawn.GlassEffect;
        PawnVision.ClientDesiredGlassValue(pawn, 255, RequestId: request.Int2);
        Assert.Equal(slope, pawn.SlopeGlassEffect);
        Assert.Equal(rampBase, pawn.BGlassEffect);
        Assert.Equal(amount, pawn.GlassEffect);
        Assert.Equal(1, TransitionSounds(scene));
    }

    [Fact]
    public void RapidReleaseReversesFadeAndEarlierActivationReplyDoesNotCloakAgain()
    {
        var (scene, pawn, net) = Client();
        SpyCloak.SetActive(pawn, true);
        PawnVision.TickGlass(pawn);
        byte visibleAmount = pawn.GlassEffect;

        SpyCloak.SetActive(pawn, false);
        Assert.Equal(0, pawn.DesiredGlassEffect);
        Assert.Equal(visibleAmount, pawn.GlassEffect);
        Assert.True(pawn.SlopeGlassEffect < 0f);
        float slope = pawn.SlopeGlassEffect;
        float rampBase = pawn.BGlassEffect;

        var requests = net.ToServer.Where(c => c.Call.Function == new Name("SetDesiredGlassValue")).ToArray();
        Assert.NotEqual(requests[0].Call.Int2, requests[1].Call.Int2);
        PawnVision.ClientDesiredGlassValue(pawn, 255, RequestId: requests[0].Call.Int2);
        Assert.Equal(0, pawn.DesiredGlassEffect);
        PawnVision.ClientDesiredGlassValue(pawn, 0, RequestId: requests[1].Call.Int2);
        Assert.Equal(0, pawn.DesiredGlassEffect);
        Assert.Equal(slope, pawn.SlopeGlassEffect);
        Assert.Equal(rampBase, pawn.BGlassEffect);
        Assert.Equal(2, TransitionSounds(scene));
        Assert.Equal(new[] { 255, 0 }, net.ToServer
            .Where(c => c.Call.Function == new Name("SetDesiredGlassValue"))
            .Select(c => c.Call.Int1));
    }

    [Fact]
    public void ServerForcedDecloakOverridesAnOutstandingActivation()
    {
        var (scene, pawn, _) = Client();
        SpyCloak.SetActive(pawn, true);
        PawnVision.TickGlass(pawn);

        PawnVision.ClientDesiredGlassValue(pawn, 0, 0.5f);

        Assert.Equal(0, pawn.DesiredGlassEffect);
        Assert.Equal(-pawn.GlassEffect / 0.5f, pawn.SlopeGlassEffect);
        Assert.Equal(2, TransitionSounds(scene));
    }

    [Fact]
    public void ObservingClientStillAnimatesBothAuthoritativeTransitions()
    {
        var (scene, pawn, net) = Client();
        pawn.Role = ENetRole.ROLE_SimulatedProxy;
        pawn.Controller = null;

        PawnVision.ClientDesiredGlassValue(pawn, 255);
        PawnVision.TickGlass(pawn);
        Assert.InRange(pawn.GlassEffect, (byte)1, (byte)254);

        PawnVision.ClientDesiredGlassValue(pawn, 0, 0.5f);
        Assert.Equal(0, pawn.DesiredGlassEffect);
        Assert.Equal(-pawn.GlassEffect / 0.5f, pawn.SlopeGlassEffect);
        Assert.Equal(2, TransitionSounds(scene));
        Assert.DoesNotContain(net.ToServer, c => c.Call.Function == new Name("SetDesiredGlassValue"));
    }

    [Fact]
    public void RoutedTransitionEchoDoesNotRestartFlickerOrSoundButActualHitsStillDo()
    {
        var (scene, pawn, net) = Client();
        pawn.Level.TimeSeconds = 10f;
        SpyCloak.SetActive(pawn, true);
        Assert.Equal(10f, pawn.PerturbationStartTime);
        Assert.Equal(1, Sounds(scene, PawnVision.SndCapeDamage));
        var request = Assert.Single(net.ToServer, c => c.Call.Function == new Name("SetDesiredGlassValue")).Call;

        var server = new CombatScene(ENetMode.NM_DedicatedServer);
        var (serverPc, serverPawn) = server.Player(true, default);
        serverPc.Player = new SimNetConnection();
        serverPawn.Level.TimeSeconds = 10.1f;
        var serverNet = new RecordingCombatNet();
        server.Level.SetService<ICombatNet>(serverNet);
        Assert.True(GadgetNetReceive.FromOwner(serverPc, request with { Target = serverPawn }));

        Assert.Equal(new[] { "SetPerturbationGlassOnClients", "SetDesiredGlassValueOnClients" },
            serverNet.ToClients.Select(c => c.Call.Function.ToString()));
        Assert.All(serverNet.ToClients, c => Assert.Equal(request.Int2, c.Call.Int2));
        var pc = Assert.IsType<SPlayerController>(pawn.Controller);
        var hit = new CombatCall(new Name("SetPerturbationGlassOnClients"))
        { Target = pawn, Float1 = 0.4f, Vector1 = new FVector(15f, 0f, 0f) };
        pawn.Level.TimeSeconds = 10.15f;
        Assert.True(GadgetNetReceive.FromServer(pc, hit));
        Assert.Equal(10.15f, pawn.PerturbationStartTime);
        Assert.Equal(2, Sounds(scene, PawnVision.SndCapeDamage));

        pawn.Level.TimeSeconds = 10.2f;
        foreach (var reply in serverNet.ToClients)
            Assert.True(GadgetNetReceive.FromServer(pc, reply.Call with { Target = pawn }));

        Assert.Equal(10.15f, pawn.PerturbationStartTime);
        Assert.Equal(0.4f, pawn.PerturbationDuration);
        Assert.Equal(15f, pawn.PerturbationValue);
        Assert.Equal(2, Sounds(scene, PawnVision.SndCapeDamage));
        Assert.Equal(1, TransitionSounds(scene));

        Assert.True(GadgetNetReceive.FromServer(pc, hit));
        Assert.Equal(10.2f, pawn.PerturbationStartTime);
        Assert.Equal(0.4f, pawn.PerturbationDuration);
        Assert.Equal(15f, pawn.PerturbationValue);
        Assert.Equal(3, Sounds(scene, PawnVision.SndCapeDamage));
    }

    [Fact]
    public void ServerAcknowledgesAnAlreadyReachedTargetOnlyToTheRequestingOwner()
    {
        var scene = new CombatScene(ENetMode.NM_DedicatedServer);
        var (owner, pawn) = scene.Player(true, default);
        owner.Player = new SimNetConnection();
        var (observer, _) = scene.Player(false, default);
        observer.Player = new SimNetConnection();
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        pawn.DesiredGlassEffect = 255;

        Assert.True(GadgetNetReceive.FromOwner(owner, new CombatCall(new Name("SetDesiredGlassValue"))
        { Target = pawn, Int1 = 255, Int2 = 7 }));

        var reply = Assert.Single(net.ToClients);
        Assert.Same(owner, reply.To);
        Assert.Equal(new Name("SetDesiredGlassValueOnClients"), reply.Call.Function);
        Assert.Equal(7, reply.Call.Int2);
        Assert.True(reply.Call.Bool1);
    }

    [Fact]
    public void AcknowledgementForSupersededReleaseDoesNotRestartForcedDecloak()
    {
        var (scene, pawn, net) = Client();
        pawn.DesiredGlassEffect = pawn.GlassEffect = 255;
        SpyCloak.SetActive(pawn, false);
        var request = Assert.Single(net.ToServer, c => c.Call.Function == new Name("SetDesiredGlassValue")).Call;
        PawnVision.ClientDesiredGlassValue(pawn, 0, 0.2f);
        float slope = pawn.SlopeGlassEffect;
        float rampBase = pawn.BGlassEffect;
        int sounds = TransitionSounds(scene);

        var server = new CombatScene(ENetMode.NM_DedicatedServer);
        var (serverPc, serverPawn) = server.Player(true, default);
        serverPc.Player = new SimNetConnection();
        var serverNet = new RecordingCombatNet();
        server.Level.SetService<ICombatNet>(serverNet);
        Assert.True(GadgetNetReceive.FromOwner(serverPc, request with { Target = serverPawn }));
        var acknowledgement = Assert.Single(serverNet.ToClients).Call;
        Assert.True(GadgetNetReceive.FromServer(Assert.IsType<SPlayerController>(pawn.Controller),
            acknowledgement with { Target = pawn }));

        Assert.Equal(0, pawn.DesiredGlassEffect);
        Assert.Equal(slope, pawn.SlopeGlassEffect);
        Assert.Equal(rampBase, pawn.BGlassEffect);
        Assert.Equal(sounds, TransitionSounds(scene));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmfVisibilityLastsThroughFadeOutAndPreservesOtherDetectionSources(bool marked)
    {
        var (_, pawn, net) = Client();
        float heat = pawn.HeatIntensity;
        float heatOpacity = pawn.HeatOpacity;
        pawn.Level.TimeSeconds = 10f;
        SpyCloak.SetActive(pawn, true);
        Assert.True(pawn.bEMFIsOn);
        Assert.Equal(1f, pawn.EMFOpacity);
        Assert.True(EmfVision.Shows(pawn));

        pawn.GlassEffect = 255;
        pawn.bSpyBulletOn = marked;
        SpyCloak.SetActive(pawn, false);
        Assert.Equal(0, pawn.DesiredGlassEffect);
        Assert.True(PawnVision.IsSpyEmfVisible(pawn));
        Assert.True(pawn.bEMFIsOn);
        Assert.Equal(1f, pawn.EMFOpacity);
        Assert.DoesNotContain(net.ToServer, c => c.Call.Function == new Name("UnSetEMFToServer"));

        // The ramp can reach zero just before the perturbation window ends; a vision switch during that
        // interval must not hide the spy before another flicker sample can make the body visible again.
        pawn.GlassEffect = 0;
        pawn.Level.TimeSeconds = 10.95f;
        PawnVision.TestAndSetEMFVisible(pawn);
        Assert.True(pawn.bEMFIsOn);

        pawn.Level.TimeSeconds = 12f;
        PawnVision.TickGlass(pawn);
        Assert.Equal(0, pawn.GlassEffect);
        Assert.Equal(marked, pawn.bEMFIsOn);
        Assert.Equal(marked ? 1f : 0f, pawn.EMFOpacity);
        Assert.Equal(marked ? 0 : 1, net.ToServer.Count(c => c.Call.Function == new Name("UnSetEMFToServer")));
        Assert.Equal(heat, pawn.HeatIntensity);
        Assert.Equal(heatOpacity, pawn.HeatOpacity);
        Assert.True(heat > 0f && heatOpacity > 0f);
    }

    [Theory]
    [InlineData(80, 255)]
    [InlineData(180, 0)]
    public void InitialObserverCloakRebasesRemainingFadeWithoutSoundsOrOwnerMutations(byte amount, byte target)
    {
        var (scene, pawn, net) = Client();
        pawn.Role = ENetRole.ROLE_SimulatedProxy;
        pawn.Level.TimeSeconds = 1000f;
        pawn.Velocity = new FVector(1000f, 0f, 0f); // An observer must not independently force its cloak off.
        float energy = pawn.EnergyLevel;
        Assert.True(GadgetNetReceive.FromServer(Assert.IsType<SPlayerController>(pawn.Controller),
            new CombatCall(new Name("InitialCloakState"))
            { Target = pawn, Int1 = amount, Int2 = target, Float1 = 0.5f, Vector1 = new FVector(0.3f, 12.8f, 0f) }));

        Assert.Equal(amount, pawn.GlassEffect);
        Assert.Equal(target, pawn.DesiredGlassEffect);
        Assert.Equal((target - amount) / 0.5f, pawn.SlopeGlassEffect);
        Assert.Equal(1000f, pawn.PerturbationStartTime);
        Assert.Equal(0, TransitionSounds(scene));
        Assert.Equal(0, Sounds(scene, PawnVision.SndCapeDamage));

        pawn.Level.TimeSeconds = 1001f;
        PawnVision.TickGlass(pawn);
        Assert.Equal(target, pawn.GlassEffect);
        Assert.Equal(target, pawn.DesiredGlassEffect);
        Assert.Equal(energy, pawn.EnergyLevel);
        Assert.Empty(net.ToServer);
    }

    private static (CombatScene Scene, SPawnAttaque Pawn, RecordingCombatNet Net) Client()
    {
        var scene = new CombatScene(ENetMode.NM_Client);
        var (pc, player) = scene.Player(true, new FVector(0f, 0f, 100f));
        var pawn = (SPawnAttaque)player;
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        pc.Role = ENetRole.ROLE_AutonomousProxy;
        pc.RemoteRole = ENetRole.ROLE_Authority;
        pawn.Role = ENetRole.ROLE_AutonomousProxy;
        pawn.RemoteRole = ENetRole.ROLE_Authority;
        pawn.WeaponLoaded = 0;
        pawn.EnergyLevel = pawn.EnergyLevelMax;
        return (scene, pawn, net);
    }

    private static int TransitionSounds(CombatScene scene) => Sounds(scene, PawnVision.SndCapeInOut);

    private static int Sounds(CombatScene scene, AssetRef sound)
    {
        int count = 0;
        foreach (SimEvent e in scene.World.Events.Current)
        {
            if (e.Kind == SimEventKind.PlaySound && e.What == new Name(sound.Path)) count++;
        }
        return count;
    }
}
