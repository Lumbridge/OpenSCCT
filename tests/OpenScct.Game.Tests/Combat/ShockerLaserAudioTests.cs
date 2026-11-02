using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Combat;

public sealed class ShockerLaserAudioTests
{
    private static readonly Name LaserSound = new("Armes_impacts.Armes.Laser_shot_loop1");

    private static (PawnRig Rig, SStickyShocker Pistol) Spy(bool crouched, bool fast)
    {
        PawnRig rig = Rig(PawnTeam.Spy, crouched: crouched);
        rig.SetFastSpeed(fast);
        CombatEquipment.Install(rig.Level);
        SStickyShocker pistol = rig.Level.SpawnActor(new SStickyShocker(), rig.Pawn.Location, rig.Pawn.Rotation)!;
        pistol.GiveTo(rig.Pawn);
        return (rig, pistol);
    }

    private static SimEvent[] LaserSounds(World world) => world.Events.Current.ToArray()
        .Where(e => e.Kind == SimEventKind.PlaySound && e.What == LaserSound).ToArray();

    private static int Step(PawnRig rig, SStickyShocker pistol, int frames, int pressFrames = 0)
    {
        int sounds = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            rig.Step(new PlayerInputFrame { bFireMode = (byte)(frame < pressFrames ? 1 : 0) });
            SimEvent[] played = LaserSounds(rig.World);
            if (played.Length > 0)
            {
                Assert.Single(played);
                Assert.True(pistol.bLaserOn);
                Assert.Equal(2, rig.Pawn.WeaponLoaded);
            }
            sounds += played.Length;
        }
        return sounds;
    }

    [Theory]
    [InlineData(false, false, 6, 1)]
    [InlineData(false, true, 6, 1)]
    [InlineData(true, false, 6, 1)]
    [InlineData(true, true, 6, 1)]
    [InlineData(false, false, 60, 0)]
    [InlineData(false, true, 60, 0)]
    [InlineData(true, false, 60, 0)]
    [InlineData(true, true, 60, 0)]
    public void DrawPlaysOnceWhenTheLaserComesOn(bool crouched, bool fast, int pressFrames, int expected)
    {
        (PawnRig rig, SStickyShocker pistol) = Spy(crouched, fast);

        Assert.Equal(expected, Step(rig, pistol, 120, pressFrames));
        Assert.Equal(2, rig.Pawn.WeaponLoaded);
        Assert.Equal(expected == 1, pistol.bLaserOn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TogglingADrawnPistolsLaserPlaysOncePerActivation(bool crouched)
    {
        (PawnRig rig, SStickyShocker pistol) = Spy(crouched, fast: false);
        Assert.Equal(0, Step(rig, pistol, 120, pressFrames: 60));
        Assert.Equal(2, rig.Pawn.WeaponLoaded);
        Assert.False(pistol.bLaserOn);

        rig.Controller.eventSwitchLaser();
        Assert.Equal(1, Step(rig, pistol, 60));
        Assert.True(pistol.bLaserOn);

        rig.Controller.eventSwitchLaser();
        Assert.Equal(0, Step(rig, pistol, 30));
        Assert.False(pistol.bLaserOn);

        rig.Controller.eventSwitchLaser();
        Assert.Equal(1, Step(rig, pistol, 60));
        Assert.True(pistol.bLaserOn);

        Assert.Equal(0, Step(rig, pistol, 60, pressFrames: 1));
        Assert.Equal(0, rig.Pawn.WeaponLoaded);
        Assert.False(pistol.bLaserOn);
    }

    [Fact]
    public void OwnerPlaysReplicatedActivationImmediatelyAndRelaysOnceWithoutAnOwnerEcho()
    {
        var scene = new CombatScene(ENetMode.NM_Client);
        var (pc, pawn) = scene.Player(true, default);
        scene.Level.Info.LocalPlayerController = pc;
        var pistol = scene.Give(new SStickyShocker(), pawn);
        pistol.GiveTo(pawn);
        pistol.Role = ENetRole.ROLE_AutonomousProxy;
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);

        pistol.bLaserOn = true;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        pistol.bLaserOn = false;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);

        SimEvent local = Assert.Single(LaserSounds(scene.World));
        Assert.True(local.Owned);
        Assert.Equal(pawn.ActorId, local.ActorId);
        Assert.Equal(10f, local.Radius);
        CombatCall relay = Assert.Single(net.ToServer).Call;
        Assert.Equal(new Name("ServerSound"), relay.Function);
        Assert.Equal(LaserSound, relay.Name1);
        Assert.True(relay.Bool1);
        Assert.Equal(10, relay.Int2);

        var server = new CombatScene(ENetMode.NM_DedicatedServer);
        var (serverPc, serverPawn) = server.Player(true, default);
        var serverPistol = server.Give(new SStickyShocker(), serverPawn);
        serverPc.ReceiveCombatCall(relay with { Target = serverPistol }, true);
        Assert.True(Assert.Single(LaserSounds(server.World)).Owned);

        pistol.bLaserOn = true;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        Assert.Equal(2, LaserSounds(scene.World).Length);
        Assert.Equal(2, net.ToServer.Count);
    }

    [Theory]
    [InlineData(ENetMode.NM_Client)]
    [InlineData(ENetMode.NM_DedicatedServer)]
    public void RemoteViewsAndDedicatedServersDoNotGenerateAnotherActivationCue(ENetMode mode)
    {
        var scene = new CombatScene(mode);
        var (_, pawn) = scene.Player(true, default);
        var (viewer, _) = scene.Player(false, default);
        scene.Level.Info.LocalPlayerController = viewer;
        var pistol = scene.Give(new SStickyShocker(), pawn);
        pistol.GiveTo(pawn);
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);

        pistol.bLaserOn = true;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        pistol.TickSWeaponAttaque(Sim.DeltaTime);

        Assert.Empty(LaserSounds(scene.World));
        Assert.Empty(net.ToServer);
    }
}
