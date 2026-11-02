using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Modes;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MercShockPresentationTests
{
    [Fact]
    public void SnapshotObserverStartsAndStopsTheSampledElectricSkinAndSparks()
    {
        using var rig = WalkerRig.Create(null, 972, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true, AdaptiveInterpolation = false });
        Level level = rig.Server.World.Level;
        level.SetService(new MovementService());
        rig.Server.PlayerJoined += slot =>
        {
            var viewer = level.Spawn<SPlayerController>()!;
            viewer.Player = new SimNetConnection(); viewer.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = viewer;
        };
        var authority = level.Spawn<SPlayerController>()!;
        var pawn = level.Spawn<SPawnDefense_1Mesh>()!;
        pawn.bAlwaysRelevant = true;
        authority.Possess(pawn);
        authority.TimeShock = -100f;
        rig.Server.BeforeWorldTick += _ =>
        {
            pawn.Physics = EPhysics.PHYS_None; pawn.Location = new FVector(100, 100, 1000);
            pawn.Velocity = default;
        };
        void Step(int n) { for (int i = 0; i < n; i++) rig.Step(); }
        Step(180);
        var observer = Assert.IsAssignableFrom<SBasePawn>(rig.Clients[0].Driver.FindActorByNetGuid((uint)pawn.ActorId));
        Assert.Equal(PawnProxyMode.Snapshots, rig.Clients[0].PawnProxies);
        Assert.False(observer.bStickySkinned);
        authority.Checkshock(2, true);
        Step(30);
        Assert.True(observer.bStickyShocking);
        Assert.True(observer.bStickySkinned);
        Assert.All(observer.Shock_SfxActors, spark => Assert.NotNull(spark));
        Assert.True(MercShockPresentation.Read(observer).Active);
        Actor?[] sparks = new Actor?[observer.Shock_SfxActors.Length];
        for (int i = 0; i < sparks.Length; i++) sparks[i] = observer.Shock_SfxActors[i];
        Step(30);
        // Drying/phospho expiry restores skins independently of the active electric emitters.
        observer.RestoreSkins();
        observer.ResetNetworkHistory();
        pawn.ResetNetworkHistory();
        Step(30);
        for (int i = 0; i < sparks.Length; i++) Assert.Same(sparks[i], observer.Shock_SfxActors[i]);
        ClientSession late = rig.AddClient();
        Step(45);
        var latePawn = Assert.IsAssignableFrom<SBasePawn>(late.Driver.FindActorByNetGuid((uint)pawn.ActorId));
        Assert.True(latePawn.bStickySkinned);
        Assert.All(latePawn.Shock_SfxActors, spark => Assert.NotNull(spark));
        authority.EndStickyShockNow();
        Step(30);
        Assert.False(observer.bStickyShocking);
        Assert.False(observer.bStickySkinned);
        Assert.All(observer.Shock_SfxActors, spark => Assert.Null(spark));
        Assert.True(MercShockPresentation.Read(observer).Active);
        Assert.False(latePawn.bStickySkinned);
        Assert.All(latePawn.Shock_SfxActors, spark => Assert.Null(spark));
        authority.TimeShock = -100f;
        authority.Checkshock(2, true);
        Step(30);
        Assert.True(observer.bStickySkinned);
        pawn.Destroy();
        pawn = level.Spawn<SPawnDefense_1Mesh>()!;
        pawn.bAlwaysRelevant = true;
        authority.Possess(pawn);
        Step(60);
        Assert.True(observer.bDeleteMe);
        Assert.All(observer.Shock_SfxActors, spark => Assert.Null(spark));
        var replacement = Assert.IsAssignableFrom<SBasePawn>(rig.Clients[0].Driver.FindActorByNetGuid((uint)pawn.ActorId));
        Assert.False(replacement.bStickySkinned);
        Assert.All(replacement.Shock_SfxActors, spark => Assert.Null(spark));
    }

    [Theory]
    [InlineData(PawnProxyMode.Snapshots, false)]
    [InlineData(PawnProxyMode.DeadReckoning, false)]
    [InlineData(PawnProxyMode.Interpolate, false)]
    [InlineData(PawnProxyMode.Snapshots, true)]
    [InlineData(PawnProxyMode.DeadReckoning, true)]
    [InlineData(PawnProxyMode.Interpolate, true)]
    public void OwnerAndObserverReceiveTimedStatusWithIndependentClocksAndResetRecovery(PawnProxyMode mode, bool spy)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 971, 2,
            new ServerSettings { SpawnWalkers = false, Net = new NetDriverSettings { PawnProxies = mode } },
            new ClientSettings { Gameplay = true, AdaptiveInterpolation = false });
        Level level = rig.Server.World.Level;
        // Shipping rate profiles force snapshots; override the immutable setting in this legacy-wire fixture before Welcome.
        typeof(NetDriverSettings).GetProperty(nameof(NetDriverSettings.PawnProxies))!.SetValue(rig.Server.Driver.Settings, mode);
        level.SetService(new MovementService());
        SPlayerController? authority = null;
        SBasePawn? pawn = null;
        int joined = 0;
        rig.Server.PlayerJoined += slot =>
        {
            var controller = level.Spawn<SPlayerController>()!;
            controller.Player = new SimNetConnection(); controller.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = controller;
            if (joined++ != 0) return;
            authority = controller;
            pawn = spy ? level.Spawn<SPawnAttaque_1Mesh>()! : level.Spawn<SPawnDefense_1Mesh>()!;
            pawn.bAlwaysRelevant = true;
            controller.Possess(pawn);
            controller.TimeShock = -100f;
        };
        rig.Server.BeforeWorldTick += _ =>
        {
            if (pawn is null) return;
            pawn.Physics = EPhysics.PHYS_None; pawn.Location = new FVector(100, 100, 1000);
            pawn.Velocity = default;
        };
        void Step(int n) { for (int i = 0; i < n; i++) rig.Step(); }
        void InjectState(SBasePawn target, uint reset, uint epoch, uint revision)
        {
            var writer = new FBitWriter(256);
            writer.WriteIntPacked(reset); writer.WriteIntPacked(epoch); writer.WriteIntPacked(revision);
            writer.WriteIntPacked((uint)level.TickCount); writer.WriteBit(false); writer.WriteBit(true);
            rig.Clients[0].Driver.Classes.Find(target.GetType())!.Layout.FindFunction("SvmMercShockStatus")!.Extension!.Receive(
                target, new FBitReader(writer.Buffer, writer.NumBits), rig.Clients[0].Driver.ServerConnection!);
        }
        Step(300);
        Assert.NotNull(authority); Assert.NotNull(pawn);
        var owner = Assert.IsAssignableFrom<SBasePawn>(rig.Clients[0].Driver.FindActorByNetGuid((uint)pawn.ActorId));
        var observer = Assert.IsAssignableFrom<SBasePawn>(rig.Clients[1].Driver.FindActorByNetGuid((uint)pawn.ActorId));
        Assert.Equal(ENetRole.ROLE_AutonomousProxy, owner.Role);
        Assert.False(MercShockPresentation.Read(owner).Active);
        Assert.False(MercShockPresentation.Read(observer).Active);
        Assert.Equal(mode, rig.Clients[0].PawnProxies);
        long messagesBefore = rig.Server.PawnStates.ShockStatusRecordsSent;
        authority.TimeShock = level.TimeSeconds; authority.RealTimeInvulSticky = 12.5f;
        long firstProtectedTick = level.TickCount + 1;
        int beforeTransition = 0;
        for (int i = 0; i < 45; i++)
        {
            rig.Step();
            if (mode != PawnProxyMode.DeadReckoning && rig.Clients[1].RenderTimeFixed < firstProtectedTick * NetTimeline.Fraction)
            {
                Assert.False(MercShockPresentation.Read(observer).Active);
                beforeTransition++;
            }
        }
        if (mode != PawnProxyMode.DeadReckoning) Assert.True(beforeTransition > 0);
        var own = MercShockPresentation.Read(owner);
        var remote = MercShockPresentation.Read(observer);
        if (spy)
        {
            Assert.True(MercShockImmunity.TimerBlocks(authority));
            Assert.False(own.Active);
            Assert.False(remote.Active);
            Assert.Equal(messagesBefore, rig.Server.PawnStates.ShockStatusRecordsSent);
            return;
        }
        Assert.True(own.Active); Assert.True(remote.Active);
        Assert.InRange(own.RemainingSeconds, 11.4f, 12.1f);
        Assert.InRange(own.Fraction, 0.9f, 0.98f);
        if (mode != PawnProxyMode.DeadReckoning) Assert.True(remote.RemainingSeconds > own.RemainingSeconds);
        // Owner controller timers are deliberately wrong: presentation must still use the server tuple.
        if (owner.Controller is SPlayerController ownerController) { ownerController.TimeShock = -10000f; ownerController.RealTimeInvulSticky = 0f; }
        float before = own.RemainingSeconds;
        Step(120);
        Assert.InRange(before - MercShockPresentation.Read(owner).RemainingSeconds, 1.9f, 2.1f);
        Assert.InRange(rig.Server.PawnStates.ShockStatusRecordsSent - messagesBefore, 1, 10);
        owner.ResetNetworkHistory();
        Assert.False(MercShockPresentation.Read(owner).Active);
        InjectState(owner, owner.NetworkResetId - 1, pawn.NetworkResetId, uint.MaxValue);
        Assert.False(MercShockPresentation.Read(owner).Active); // stale pre-reset reliable update cannot revive the cue
        Step(60);
        Assert.True(MercShockPresentation.Read(owner).Active);
        observer.ResetNetworkHistory();
        Assert.False(MercShockPresentation.Read(observer).Active);
        Step(60);
        Assert.True(MercShockPresentation.Read(observer).Active);
        level.Info.TimeDilation = 0.5f;
        Step(30);
        before = MercShockPresentation.Read(owner).RemainingSeconds;
        Step(120);
        Assert.InRange(before - MercShockPresentation.Read(owner).RemainingSeconds, 0.9f, 1.1f);
        var practice = new PracticeRules { On = true };
        level.SetService(practice); practice.SetTank(authority, true);
        authority.TimeShock = -100f;
        Step(45);
        Assert.True(MercShockPresentation.Read(owner).StateProtected);
        Assert.True(MercShockPresentation.Read(observer).StateProtected);
        Assert.Equal(0f, MercShockPresentation.Read(observer).RemainingSeconds);
        practice.SetTank(authority, false);
        authority.TimeShock = level.TimeSeconds; authority.RealTimeInvulSticky = 3f;
        pawn.ResetNetworkHistory();
        Step(45);
        Assert.True(MercShockPresentation.Read(owner).Active);
        Assert.False(MercShockPresentation.Read(owner).StateProtected);
        Assert.True(MercShockPresentation.Read(observer).Active);
        ClientSession late = rig.AddClient();
        Step(60);
        var latePawn = Assert.IsAssignableFrom<SBasePawn>(late.Driver.FindActorByNetGuid((uint)pawn.ActorId));
        Assert.True(MercShockPresentation.Read(latePawn).Active);
        Assert.InRange(MercShockPresentation.Read(latePawn).RemainingSeconds, 1.8f, 2.5f);
        Step(450);
        Assert.False(MercShockPresentation.Read(owner).Active);
        Assert.False(MercShockPresentation.Read(observer).Active);
        InjectState(owner, owner.NetworkResetId, pawn.NetworkResetId - 1, uint.MaxValue);
        Assert.False(MercShockPresentation.Read(owner).Active); // a previous authoritative lifetime is also rejected
        SBasePawn oldOwner = owner;
        pawn.Destroy();
        pawn = spy ? level.Spawn<SPawnAttaque_1Mesh>()! : level.Spawn<SPawnDefense_1Mesh>()!;
        pawn.bAlwaysRelevant = true; authority.Possess(pawn); authority.TimeShock = -100f;
        Step(60);
        owner = Assert.IsAssignableFrom<SBasePawn>(rig.Clients[0].Driver.FindActorByNetGuid((uint)pawn.ActorId));
        Assert.NotSame(oldOwner, owner);
        Assert.False(MercShockPresentation.Read(owner).Active);
        Assert.False(MercShockPresentation.Read(oldOwner).Active);
    }
}
