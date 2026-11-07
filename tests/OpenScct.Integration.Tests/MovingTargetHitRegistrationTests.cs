using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MovingTargetHitRegistrationTests(ITestOutputHelper output)
{
    private sealed class Traces(IHitZones inner) : IHitZones
    {
        public BoneTraceResult Last;
        public long WorldTick, CommandTick;
        public BoneTraceResult TraceBone(Actor shooter, FVector end, FVector start, bool real)
        {
            WorldTick = shooter.XLevel.WorldTickCount; CommandTick = shooter.XLevel.TickCount;
            return Last = inner.TraceBone(shooter, end, start, real);
        }
    }
    [Theory]
    [InlineData(ENetMode.NM_ListenServer, 35)]
    [InlineData(ENetMode.NM_ListenServer, 200)]
    [InlineData(ENetMode.NM_DedicatedServer, 35)]
    [InlineData(ENetMode.NM_DedicatedServer, 200)]
    public void MercShotRewindsMovingSpyAfterJoiningAnEstablishedHost(ENetMode mode, int ping)
    {
        using var rig = WalkerRig.Create(new SimLinkProfile("moving target", ping, 10, 0), 379, 0,
            new ServerSettings
            {
                GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=4?LivesDEF=4",
                PlayersToStart = 1, Profile = NetProfile.Enhanced, NetMode = mode, UseStartGate = true,
            }, new ClientSettings { Gameplay = true, LoginOptions = "?Team=1" });
        // A server's lifetime and a newly connected player's command clock are deliberately different.
        for (int i = 0; i < 600; i++) rig.Step();
        ClientSession client = rig.AddClient("Merc");
        for (int i = 0; i < 900; i++) rig.Step();
        var owner = Assert.IsType<SPlayerController>(client.LocalPlayer);
        var shooter = Assert.IsAssignableFrom<SPAWNDEFENSE>(owner.Pawn);
        var rifle = Assert.IsType<SWeaponDefense>(shooter.MyWeapon);
        var authority = Assert.IsType<SPlayerController>(rig.Server.Players.Single().Controller);
        var serverRifle = Assert.IsType<SWeaponDefense>(((SBasePawn)authority.Pawn!).MyWeapon);
        rifle.MinDispersion = rifle.MaxDispersion = serverRifle.MinDispersion = serverRifle.MaxDispersion = 0;
        var target = rig.Server.World.Level.Spawn<SPawnAttaque>()!;
        target.Health = 10000;
        target.SetPhysics(EPhysics.PHYS_None);
        FVector origin = authority.Pawn!.Location + new FVector(300, 0, 0);
        long start = rig.Server.World.TickCount;
        rig.Server.BeforeWorldTick += host =>
        {
            target.Location = origin + new FVector(0, (host.World.TickCount - start - 90) * 6, 0);
            target.Velocity = new FVector(0, 360, 0);
        };
        for (int i = 0; i < 90; i++) rig.Step();
        var proxies = new List<SPawnAttaque>();
        foreach (var pawn in client.World.Level.DynamicActors<SPawnAttaque>()) proxies.Add(pawn);
        var proxy = Assert.Single(proxies);
        Assert.True(proxy.NetworkPoseReady);
        Assert.True(client.PawnStates!.IsMotionBuffered(proxy, client.RenderTimeFixed));
        var pills = new List<PillShape>();
        client.World.Level.GetService<IPillPose>()!.GetPills(proxy, pills);
        PillShape chest = pills.Single(p => p.Pill == 2);
        FVector aimAt = (chest.A + chest.B) * 0.5f;
        FVector eye = shooter.Location + CombatServices.Ports(rifle).EyePosition(shooter);
        FRotator aim = (aimAt - eye).Rotation();
        var traces = new Traces(CombatServices.HitZones(serverRifle));
        rig.Server.World.Level.SetService<IHitZones>(traces);
        client.ReportRenderedFrame(1f);
        long shotTime = client.ViewTimeFixed;
        rifle.ServerSetAccurateRotation(aim.Yaw, aim.Pitch, aim.Roll);
        rifle.ServerFireWeapon();
        for (int i = 0; i < 45; i++) rig.Step();
        output.WriteLine($"view={shotTime / 256.0} world={traces.WorldTick} input={traces.CommandTick} proxy={proxy.Location} aim={aimAt} hit={traces.Last.Actor} location={traces.Last.HitLocation}");
        Assert.Equal(1, serverRifle.ShotCounter);
        Assert.True(rig.Server.LagCompensation.RewoundShots > 0);
        Assert.Same(target, traces.Last.Actor);
        // This scripted target has no controller; HitPawn still records the weapon after resolving the trace.
        Assert.Same(serverRifle, target.LastBulletWeapon);
    }
}
