using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class CloakVisionNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void CloakAndFadeStayDetectableOnMercClientsAndLateJoiners(NetProfile profile)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 742, SimLinkProfile.Ping200);
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes, Profile = profile,
            Bots = [new("Spy", BotTeam.Spy, () => new IdleBrain()), new("Merc", BotTeam.Merc, () => new IdleBrain())],
        }, network.Bind, "server", 0);
        ClientSession? observer = null;
        long frame = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Step(clock.NowMicroseconds);
                observer?.Tick(clock.NowMicroseconds);
            }
        }

        Tick(900);
        var spyBot = match.Bots[0];
        var owner = Assert.IsAssignableFrom<SPawnAttaque>(spyBot.Controller!.Pawn);
        var authority = Assert.IsAssignableFrom<SPawnAttaque>(match.ServerController(spyBot)!.Pawn);
        var merc = match.Bots[1].Session;
        authority.bAlwaysRelevant = true;
        Tick(60); // Both viewers must have the pawn channel before the cloak broadcast is made.
        var serverBridge = match.Server.World.Level.GetService<NetCombatBridge>()!;
        var mercBridge = merc.World.Level.GetService<NetCombatBridge>()!;
        serverBridge.Trace = [];
        mercBridge.Trace = [];
        owner.WeaponLoaded = 0;
        const float initialEnergy = 60.25f; // Partial charge must work without waiting for a full battery.
        owner.EnergyLevel = initialEnergy;
        float serverEnergy = authority.EnergyLevel;
        float mercCopyEnergy = Copy(merc).EnergyLevel;
        SpyCloak.SetActive(owner, true);
        Assert.Equal(255, owner.DesiredGlassEffect);
        Assert.True(owner.bEMFIsOn);
        Assert.Equal(1f, owner.EMFOpacity);
        Tick(120);
        Assert.InRange(owner.EnergyLevel, initialEnergy - 16.15f, initialEnergy - 15.85f);
        CheckRemoteEnergy();
        Assert.Equal(255, owner.GlassEffect);
        Assert.True(Copy(merc).GlassEffect == 255,
            $"owner {owner.DesiredGlassEffect}/{owner.GlassEffect}, server {authority.DesiredGlassEffect}/{authority.GlassEffect}, " +
            $"merc {Copy(merc).DesiredGlassEffect}/{Copy(merc).GlassEffect}/{Copy(merc).Role}, " +
            $"server calls {string.Join(';', serverBridge.Trace)}, merc calls {string.Join(';', mercBridge.Trace)}");
        Check(authority, true);
        Check(Copy(merc), true);

        using var late = new ClientSession(network.Bind("late"), "server", clock.NowMicroseconds,
            new ClientSettings
            {
                PlayerName = "LateMercViewer", Gameplay = true, Classes = WalkerRig.Classes, Maps = WalkerRig.Maps,
                LoginOptions = "?SpectatorOnly=true?Team=255",
            }, ownsTransport: true);
        observer = late;
        Tick(240);
        Assert.True(late.State == EClientState.Joined, late.RejectReason);
        Check(Copy(late), true);
        Assert.Equal(255, Copy(late).GlassEffect);
        float lateCopyEnergy = Copy(late).EnergyLevel;
        Assert.InRange(owner.EnergyLevel, initialEnergy - 48.15f, initialEnergy - 47.85f);
        CheckRemoteEnergy();

        float energyAtRelease = owner.EnergyLevel;
        SpyCloak.SetActive(owner, false);
        Assert.True(owner.GlassEffect > 0);
        Check(owner, true);
        Tick(24);
        Assert.Equal(energyAtRelease, owner.EnergyLevel, 3); // Decloaking holds the battery until the fade finishes.
        // Reliable equipment RPCs follow the input-command fence. Under Stock's constrained
        // link, allow up to one second for delivery, while still requiring an unfinished fade.
        int deliveryTicks = 24;
        while (Copy(merc).DesiredGlassEffect != 0 && deliveryTicks < 60)
        {
            Tick(1);
            deliveryTicks++;
        }
        Assert.Equal(0, Copy(merc).DesiredGlassEffect);
        Assert.True(Copy(merc).GlassEffect > 0);
        Check(authority, true);
        Check(Copy(merc), true);
        Check(Copy(late), true);

        Tick(144 - deliveryTicks); // Preserve the original total fade-completion deadline.
        Assert.Equal(0, owner.GlassEffect);
        Assert.Equal(0, Copy(merc).GlassEffect);
        Assert.Equal(0, Copy(late).GlassEffect);
        Check(owner, false);
        Check(authority, false);
        Check(Copy(merc), false);
        Check(Copy(late), false);
        Assert.False(SpyCloak.IsEngaged(owner));
        float rechargingEnergy = owner.EnergyLevel;
        Tick(30);
        Assert.InRange(owner.EnergyLevel, rechargingEnergy + 3.35f, rechargingEnergy + 3.65f);
        CheckRemoteEnergy();
        Assert.Equal(lateCopyEnergy, Copy(late).EnergyLevel);

        SPawnAttaque Copy(ClientSession client) => Assert.IsAssignableFrom<SPawnAttaque>(
            client.Driver.FindActorByNetGuid((uint)authority.ActorId));

        void CheckRemoteEnergy()
        {
            // The server and observers receive cloak state, but never spend or replicate the owner's battery.
            Assert.Equal(serverEnergy, authority.EnergyLevel);
            Assert.Equal(mercCopyEnergy, Copy(merc).EnergyLevel);
        }

        static void Check(SBasePawn pawn, bool emf)
        {
            Assert.Equal(emf, pawn.bEMFIsOn);
            Assert.Equal(emf ? 1f : 0f, pawn.EMFOpacity);
            Assert.Equal(emf, EmfVision.Shows(pawn));
            Assert.Equal(1f, pawn.HeatIntensity);
            Assert.Equal(1f, pawn.HeatOpacity);
        }
    }
}
