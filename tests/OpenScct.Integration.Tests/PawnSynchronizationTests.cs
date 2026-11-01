using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class PawnSynchronizationTests
{
    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void StanceTransitionsInterpolateTheMeshAnchorWithoutSinkingTheCollisionShape(NetProfile profile)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 423, 1,
            new ServerSettings { SpawnWalkers = false, Profile = profile }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        var pawn = level.Spawn<SPawnAttaque>()!;
        pawn.bAlwaysRelevant = true; pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        rig.Server.BeforeWorldTick += host =>
        {
            bool crouched = ((host.World.TickCount + 1) / 17 & 1) != 0;
            pawn.Physics = EPhysics.PHYS_None;
            pawn.bIsCrouched = crouched;
            pawn.ZoneConfig = (byte)(crouched ? 1 : 0);
            pawn.CollisionHeight = crouched ? 50 : 87.5f;
            pawn.PrePivot = new FVector(0, 0, crouched ? 37.5f : 0);
            pawn.Location = new FVector(100, 100, 1000 + pawn.CollisionHeight);
            pawn.Velocity = default;
        };
        int samples = 0;
        for (int n = 0; n < 1000; n++)
        {
            rig.Step();
            ClientSession client = rig.Clients[0];
            if (n < 240 || client.Driver.FindActorByNetGuid((uint)pawn.ActorId) is not SBasePawn copy ||
                !client.PawnStates!.IsMotionBuffered(copy, client.RenderTimeFixed)) continue;
            Assert.InRange(MathF.Abs(copy.Location.Z - copy.CollisionHeight - 1000), 0, 0.1f);
            Assert.InRange(MathF.Abs(copy.Location.Z + copy.PrePivot.Z - 1087.5f), 0, 0.1f);
            samples++;
        }
        Assert.True(samples > 500, $"Only {samples} buffered stance samples.");
    }

    private readonly record struct BodyState(EPhysics Physics, byte Zone, bool Crouched, bool Sleeping, bool Lying, bool Hidden,
        bool Boost, bool Hoist, bool Grabbable, bool Terminable, bool Shocking, int Health,
        float Radius, float Height, FVector Pivot, NetworkPawnAttachments Attachments, bool Paired)
    {
        public static BodyState Capture(SBasePawn pawn) => new(pawn.Physics, pawn.ZoneConfig, pawn.bIsCrouched,
            pawn.bSleeping, pawn.bLyingOnGround, pawn.bHidden, pawn.bReadyForShortLadder, pawn.bReadyForHoistClimb,
            pawn.bCanBeGrabbed, pawn.bCanBeTerminated, pawn.bStickyShocking, pawn.Health,
            pawn.CollisionRadius, pawn.CollisionHeight, pawn.PrePivot, NetworkPawnAttachments.For(pawn),
            pawn.m_AttackTarget is not null);
    }

    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void BodyTransitionsBetweenSnapshotsMatchDisplayedServerHistory(NetProfile profile)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 417, 1,
            new ServerSettings { SpawnWalkers = false, Profile = profile }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        var pawn = level.Spawn<SPawnDefense_1Mesh>()!;
        var partner = level.Spawn<SPawnAttaque>()!;
        pawn.bAlwaysRelevant = partner.bAlwaysRelevant = true;
        pawn.RemoteRole = partner.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        var dummy = level.SpawnActor(new SWeaponDefenseDummy(), default, default, pawn, pawn)!;
        pawn.WeaponDummy = dummy;
        var history = new BodyState[1200];
        rig.Server.BeforeWorldTick += host =>
        {
            // Seventeen ticks deliberately crosses both rate presets' snapshot intervals.
            bool alternate = ((host.World.TickCount + 1) / 17 & 1) != 0;
            pawn.Physics = partner.Physics = EPhysics.PHYS_None;
            pawn.Location = new FVector(100, 100, 1000); partner.Location = new FVector(300, 100, 1000);
            pawn.bIsCrouched = pawn.bSleeping = pawn.bLyingOnGround = pawn.bHidden = alternate;
            pawn.ZoneConfig = (byte)(alternate ? 1 : 0);
            pawn.bReadyForShortLadder = pawn.bCanBeGrabbed = alternate;
            pawn.bReadyForHoistClimb = pawn.bCanBeTerminated = pawn.bStickyShocking = !alternate;
            pawn.Health = alternate ? 73 : 100;
            pawn.CollisionRadius = alternate ? 23 : 24; pawn.CollisionHeight = alternate ? 30 : 48;
            pawn.PrePivot = new FVector(0, 0, alternate ? 12 : 0);
            pawn.ActiveWeaponSlot = alternate ? 1 : 0; dummy.iState = alternate ? 1 : 0;
            pawn.m_AttackTarget = pawn.m_InteractionTarget = alternate ? partner : null;
            pawn.UsedObject = alternate ? partner : null;
        };
        int comparisons = 0, boundaries = 0;
        for (int n = 0; n < 1000; n++)
        {
            rig.Step(afterServerTick: () => history[rig.Server.World.TickCount] = BodyState.Capture(pawn));
            ClientSession client = rig.Clients[0];
            if (n < 240 || client.Driver.FindActorByNetGuid((uint)pawn.ActorId) is not SBasePawn copy ||
                !client.PawnStates!.IsPoseBuffered(copy, client.RenderTimeFixed)) continue;
            int tick = (int)(client.RenderTimeFixed / NetTimeline.Fraction);
            Assert.Equal(history[tick], BodyState.Capture(copy));
            if (history[tick] != history[tick - 1]) boundaries++;
            if (copy.m_AttackTarget is not null)
            {
                Assert.Same(copy.m_AttackTarget, copy.m_InteractionTarget);
                Assert.Same(copy.m_AttackTarget, copy.UsedObject);
            }
            comparisons++;
        }
        Assert.True(comparisons > 500, $"only {comparisons} buffered samples");
        Assert.True(boundaries > 15, $"only {boundaries} exact transition boundaries");
        Assert.True(rig.Clients[0].PawnStates!.PresentationRecordsReceived > 30);
        Assert.Equal(0, rig.Server.PawnStates!.HistoryResyncs);
    }

    [Fact]
    public void FirstRelevancyStartsAtACurrentBaselineWithoutReportingExpiredUnsentTransitionsAsAResync()
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 419, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        var pawn = level.Spawn<SPawnAttaque>()!;
        pawn.bAlwaysRelevant = true; pawn.RemoteRole = ENetRole.ROLE_None;
        pawn.Physics = EPhysics.PHYS_None;
        PawnClip wait = PawnActions.Clip(pawn, new Name("WaitStAlFd0"));
        PawnClip walk = PawnActions.Clip(pawn, new Name("WalkStAlFd0"));
        bool author = true;
        rig.Server.BeforeWorldTick += _ =>
        {
            if (!author) return;
            pawn.PawnAnim.Play(0, wait, 1, 0, loop: true);
            pawn.PawnAnim.Play(0, walk, 1, 0, loop: true);
        };
        for (int n = 0; n < 300; n++) rig.Step();
        author = false; pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        for (int n = 0; n < 180; n++) rig.Step();
        SBasePawn copy = Assert.IsType<SPawnAttaque>(rig.Clients[0].Driver.FindActorByNetGuid((uint)pawn.ActorId));
        Assert.True(copy.NetworkPoseReady);
        Assert.Equal(walk.Name, copy.PawnAnim[0].Sequence);
        Assert.Equal(0, rig.Server.PawnStates.HistoryResyncs);
    }

    [Fact]
    public void HistoricalPairReferencesResolveWhenTheirActorArrivesBeforeItsSpawnAcknowledgement()
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 418, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        var pawn = level.Spawn<SPawnAttaque>()!;
        var partner = level.Spawn<SPawnDefense_1Mesh>()!;
        pawn.bAlwaysRelevant = partner.bAlwaysRelevant = true;
        pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy; partner.RemoteRole = ENetRole.ROLE_None;
        rig.Server.BeforeWorldTick += _ =>
        {
            pawn.Physics = partner.Physics = EPhysics.PHYS_None;
            pawn.Location = new FVector(100, 100, 1000); partner.Location = new FVector(300, 100, 1000);
            pawn.m_AttackTarget = pawn.m_InteractionTarget = partner;
            pawn.UsedObject = partner;
        };
        for (int n = 0; n < 300; n++) rig.Step();
        ClientSession client = rig.Clients[0];
        SBasePawn copy = Assert.IsType<SPawnAttaque>(client.Driver.FindActorByNetGuid((uint)pawn.ActorId));
        Assert.Null(copy.m_AttackTarget);
        partner.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        bool resolvedBeforeAcknowledgement = false;
        for (int n = 0; n < 180; n++)
        {
            rig.Step();
            if (client.Driver.FindActorByNetGuid((uint)partner.ActorId) is not SBasePawn partnerCopy) continue;
            if (rig.Server.Players[0].Connection.FindActorChannel(partner)?.OpenAcked != true &&
                ReferenceEquals(copy.m_AttackTarget, partnerCopy)) resolvedBeforeAcknowledgement = true;
        }
        Assert.True(resolvedBeforeAcknowledgement, "timestamped references waited for a later server mapping acknowledgement");
        Assert.Same(copy.m_AttackTarget, copy.m_InteractionTarget);
        Assert.Same(copy.m_AttackTarget, copy.UsedObject);
        Assert.True(pawn.Destroy());
        for (int n = 0; n < 120; n++) rig.Step();
        Assert.True(copy.bDeleteMe);
        Assert.Equal(-1, client.PawnStates!.NewestTick(copy));
        Assert.Equal(-1, rig.Server.PawnStates.NewestTick(pawn));
    }

    [Theory]
    [InlineData("100")]
    [InlineData("200")]
    public void FirstSeenKnockoutRetainsItsWeaponAttachmentAndSlotAtPresentationTime(string link)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Get(link), 197, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        var pawn = level.Spawn<SPawnDefense_1Mesh>()!;
        pawn.Physics = EPhysics.PHYS_None; pawn.bLyingOnGround = true; pawn.WeaponLoaded = 0;
        pawn.bAlwaysRelevant = true; pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        var dummy = level.SpawnActor(new SWeaponDefenseDummy { iState = 1 }, default, default, pawn, pawn)!;
        pawn.WeaponDummy = dummy;
        for (int i = 0; i < 300; i++) rig.Step();
        var copy = Assert.IsType<SPawnDefense_1Mesh>(rig.Clients[0].Driver.FindActorByNetGuid((uint)pawn.ActorId));
        Assert.True(copy.NetworkPoseReady);
        Assert.True(copy.bLyingOnGround);
        Assert.Equal(new NetworkPawnAttachments(0, true), NetworkPawnAttachments.For(copy));
        // An unrelated property packet can arrive ahead of the displayed pawn time. Attachment selection must
        // continue to use the coherent snapshot, including when the weapon actor's own tick has not run yet.
        copy.ActiveWeaponSlot = 1;
        Assert.Equal(0, NetworkPawnAttachments.For(copy).ActiveSlot);
        pawn.ActiveWeaponSlot = 1; dummy.iState = 0;
        for (int i = 0; i < 90; i++) rig.Step();
        Assert.Equal(new NetworkPawnAttachments(1, false), NetworkPawnAttachments.For(copy));
    }

    [Fact]
    public void SeveralAuthoredPlaysAndAClearInOneTickArriveOnceInOrder()
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Stress300, 84, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        var pawn = level.Spawn<SPawnAttaque>()!;
        pawn.bAlwaysRelevant = true; pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        pawn.Physics = EPhysics.PHYS_None;
        PawnClip wait = PawnActions.Clip(pawn, new Name("WaitStAlFd0"));
        PawnClip walk = PawnActions.Clip(pawn, new Name("WalkStAlFd0"));
        PawnClip jump = PawnActions.Clip(pawn, new Name("jumpstalfdl"));
        pawn.PawnAnim.Play(0, wait, 1f, 0f, loop: true);
        for (int i = 0; i < 300; i++) rig.Step();
        var received = new List<Name>();
        rig.Clients[0].PawnStates!.TransitionReceived += (copy, channel, state) =>
        {
            if (channel == 0 && ReferenceEquals(copy, rig.Clients[0].Driver.FindActorByNetGuid((uint)pawn.ActorId)))
                received.Add(state.Sequence);
        };
        bool batch = true;
        rig.Server.BeforeWorldTick += _ =>
        {
            if (!batch) return;
            batch = false;
            pawn.PawnAnim.Play(0, walk, 1f, 0f, loop: false);
            pawn.PawnAnim.Play(0, jump, 1f, 0f, loop: false);
            pawn.PawnAnim.Play(0, wait, 1f, 0f, loop: true);
            pawn.PawnAnim.Stop(0);
            pawn.PawnAnim.Play(0, wait, 1f, 0f, loop: true);
        };
        for (int i = 0; i < 180; i++) rig.Step();
        Assert.Equal(new[] { walk.Name, jump.Name, wait.Name, Name.None, wait.Name }, received);
        SBasePawn copy = Assert.IsType<SPawnAttaque>(rig.Clients[0].Driver.FindActorByNetGuid((uint)pawn.ActorId));
        Assert.Equal(wait.Name, copy.PawnAnim[0].Sequence);
        Assert.True(rig.SimLink!.Duplicated > 0);
        Assert.True(rig.SimLink.Reordered > 0);
    }

    [Fact]
    public void ModernPawnCaptureAndReliableTransitionsAllocateNoServerMemoryAfterWarmup()
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 38, 1,
            new ServerSettings { SpawnWalkers = false, Profile = NetProfile.Enhanced }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        var pawn = level.Spawn<SPawnAttaque>()!;
        pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy; pawn.bAlwaysRelevant = true; pawn.Physics = EPhysics.PHYS_None;
        PawnClip? wait = PawnActions.Clip(pawn, new Name("WaitStAlFd0"));
        PawnClip? walk = PawnActions.Clip(pawn, new Name("WalkStAlFd0"));
        rig.Server.BeforeWorldTick += host =>
        {
            pawn.Location = new FVector(host.World.TickCount, 0, 1000);
            if (host.World.TickCount % 30 == 0)
                pawn.PawnAnim.Play(0, host.World.TickCount % 60 == 0 ? wait : walk, 1f, 0f, loop: true);
        };
        for (int i = 0; i < 600; i++) rig.Step();
        long allocated = 0;
        for (int i = 0; i < 600; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            rig.TickServerOnly();
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            rig.TickClients(null);
        }
        Assert.Equal(0, allocated);
        Assert.InRange(rig.SimLink!.MaximumDatagramBytes, 1, rig.Server.Driver.MaxPacketBytes);
        Assert.True(rig.Clients[0].PawnStates!.AnimationRecords > 30);
    }

    [Theory]
    [InlineData(EPhysics.PHYS_None)]
    [InlineData(EPhysics.PHYS_Walking)]
    [InlineData(EPhysics.PHYS_Falling)]
    [InlineData(EPhysics.PHYS_RootMotion)]
    [InlineData(EPhysics.PHYS_OnPipe)]
    [InlineData(EPhysics.PHYS_OnPipeH)]
    [InlineData(EPhysics.PHYS_OnVerticalPlane)]
    [InlineData(EPhysics.PHYS_OnLadder)]
    [InlineData(EPhysics.PHYS_Ledge)]
    [InlineData(EPhysics.PHYS_FollowParent)]
    [InlineData(EPhysics.PHYS_Repositionnal)]
    [InlineData(EPhysics.PHYS_Vandamme)]
    public void FirstSeenPoseIncludesEveryChannelAndRecoversAcrossResetAndLateReferences(EPhysics physics)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 97, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        var pawn = level.Spawn<SPawnAttaque>()!;
        var partner = level.Spawn<SPawnDefense_1Mesh>()!;
        pawn.bAlwaysRelevant = partner.bAlwaysRelevant = true;
        pawn.RemoteRole = partner.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        bool reset = false;
        rig.Server.BeforeWorldTick += _ =>
        {
            pawn.Location = new FVector(reset ? 1200f : 100f, 100f, 1000f);
            pawn.Rotation = new FRotator(0, reset ? 54321 : 12345, 0);
            pawn.iDesiredYawPitch = 0x12343039; pawn.iYawPitchFromView = 0x43215432;
            partner.Location = new FVector(1400f, 100f, 1000f);
            pawn.Physics = physics; partner.Physics = EPhysics.PHYS_None;
            pawn.Health = 0; pawn.bLyingOnGround = true; pawn.bIsCrouched = true;
            pawn.bSleeping = true; pawn.m_InteractionTarget = partner; pawn.UsedObject = partner;
            pawn.bReadyForShortLadder = !reset; pawn.bReadyForHoistClimb = reset;
            pawn.bCanBeGrabbed = !reset; pawn.bCanBeTerminated = reset; pawn.bStickyShocking = reset;
            pawn.WeaponLoaded = 7; pawn.IK_Type = IK_Type_.IKT_NONE;
            pawn.CollisionRadius = 24f; pawn.CollisionHeight = 30f; pawn.PrePivot = new FVector(0, 0, 12);
            pawn.m_AttackTarget = partner; pawn.SetBase(partner, bNotifyActor: false);
            for (int i = 0; i < PawnAnimation.ChannelCount; i++)
                new NetworkAnimChannel
                {
                    Sequence = new Name(reset ? "WalkStAlFd0" : "WaitStAlFd0"), StartBone = new Name("B Spine1"),
                    Frame = 0.25f, Alpha = 0.6f, Loop = true, Mirror = true, Serial = reset ? 9 : 3,
                }.Apply(pawn, i, presentationOnly: false);
            pawn.NetworkRootMotion.Restore(new PawnRootMotion.State(3, new FVector(reset ? -1 : 1, 2, 3),
                new FVector(4, -5, 6.125f), reset ? -13.25f : 13f, reset ? -123 : 123, reset ? -12 : 12, reset ? 9 : 3));
        };
        void Verify(string clip)
        {
            SBasePawn copy = Assert.IsType<SPawnAttaque>(rig.Clients[0].Driver.FindActorByNetGuid((uint)pawn.ActorId));
            Assert.Equal(physics, copy.Physics); Assert.True(copy.bIsCrouched); Assert.True(copy.bLyingOnGround);
            Assert.Equal(0, copy.Health); Assert.Equal(7, copy.WeaponLoaded);
            Assert.Equal(pawn.iDesiredYawPitch, copy.iDesiredYawPitch);
            Assert.Equal(pawn.iYawPitchFromView, copy.iYawPitchFromView);
            Assert.True(copy.bSleeping); Assert.NotNull(copy.m_InteractionTarget);
            Assert.Equal(!reset, copy.bReadyForShortLadder); Assert.Equal(reset, copy.bReadyForHoistClimb);
            Assert.Equal(!reset, copy.bCanBeGrabbed); Assert.Equal(reset, copy.bCanBeTerminated); Assert.Equal(reset, copy.bStickyShocking);
            Assert.Same(copy.m_InteractionTarget, copy.UsedObject);
            Assert.Equal(30f, copy.CollisionHeight); Assert.Equal(pawn.PrePivot, copy.PrePivot);
            Assert.True((copy.m_AttackTarget is not null) == (pawn.m_AttackTarget is not null),
                $"sourceTarget={pawn.m_AttackTarget?.ActorId} partnerDeleted={partner.bDeleteMe} " +
                $"mapped={rig.Server.Players[0].Connection.FindActorChannel(partner)?.OpenAcked} " +
                $"receivedPartner={rig.Clients[0].Driver.FindActorByNetGuid((uint)partner.ActorId) is not null} " +
                $"newest={rig.Clients[0].PawnStates!.NewestTick(copy)} render={rig.Clients[0].RenderTimeFixed / NetTimeline.Fraction}");
            Assert.Equal(pawn.Base is not null, copy.Base is not null);
            if (pawn.m_AttackTarget is not null && pawn.Base is not null) Assert.Same(copy.m_AttackTarget, copy.Base);
            Assert.Equal(pawn.NetworkRootMotion.Capture(), copy.NetworkRootMotion.Capture());
            for (int i = 0; i < PawnAnimation.ChannelCount; i++)
            {
                Assert.Equal(new Name(clip), copy.PawnAnim[i].Sequence);
                Assert.Equal(new Name("B Spine1"), copy.PawnAnim[i].StartBone);
                Assert.True(copy.PawnAnim[i].bMirror);
                Assert.InRange(copy.PawnAnim[i].Frame, 0.2499f, 0.2501f);
                Assert.True(copy.PawnAnim[i].bNotifyIgnore);
            }
        }
        for (int i = 0; i < 360; i++) rig.Step();
        Verify("WaitStAlFd0");
        reset = true; pawn.ResetNetworkHistory();
        for (int i = 0; i < 180; i++) rig.Step();
        Verify("WalkStAlFd0");
        Assert.True(rig.Clients[0].PawnStates!.Resets >= 2);
    }

    private readonly ITestOutputHelper _output;
    public PawnSynchronizationTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(NetProfile.Stock, "100")]
    [InlineData(NetProfile.Stock, "150")]
    [InlineData(NetProfile.Stock, "200")]
    [InlineData(NetProfile.Enhanced, "100")]
    [InlineData(NetProfile.Enhanced, "150")]
    [InlineData(NetProfile.Enhanced, "200")]
    [InlineData(NetProfile.Enhanced, "300")]
    public void FourViewersSeeTimeAlignedMovementAndAnimationAndRecoverAfterAnOutage(NetProfile profile, string link)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Get(link), 72, 4,
            new ServerSettings { SpawnWalkers = false, Profile = profile }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            pc.Player = new SimNetConnection();
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        var pawns = new SBasePawn[3];
        for (int i = 0; i < pawns.Length; i++)
        {
            var pawn = level.Spawn<SPawnAttaque>()!;
            pawn.bAlwaysRelevant = true; pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy;
            pawn.Physics = EPhysics.PHYS_None; pawn.Health = 100;
            pawn.PawnAnim.Play(0, PawnActions.Clip(pawn, new Name("WaitStAlFd0")), -1f / 60f, 0f, loop: true);
            pawns[i] = pawn;
        }
        rig.Server.BeforeWorldTick += host =>
        {
            for (int i = 0; i < pawns.Length; i++)
            {
                pawns[i].Location = new FVector(100f + host.World.TickCount + 1, 150f * i, 1000f);
                pawns[i].Velocity = new FVector(60f, 0f, 0f);
            }
        };
        var phases = new float[1200];
        var samples = new int[4];
        var bufferedSamples = new int[4];
        var previousPositions = new float[4];
        var previousPhases = new float[4];
        var previousSamples = new int[4];
        float worstMotion = 0f, worstPhase = 0f;
        for (int n = 0; n < 1000; n++)
        {
            if (n == 600) rig.SimLink!.PauseLink("server", "client0", rig.Clock.NowMicroseconds + 250_000);
            rig.Step(afterServerTick: () => phases[rig.Server.World.TickCount] = pawns[0].PawnAnim[0].Frame);
            foreach (ClientSession client in rig.Clients)
            {
                Assert.NotEqual(EClientState.Disconnected, client.State);
                Assert.NotEqual(EClientState.Rejected, client.State);
            }
            if (n < 240 || n is >= 600 and < 660) continue;
            for (int c = 0; c < rig.Clients.Length; c++)
            {
                ClientSession client = rig.Clients[c];
                SBasePawn? copy = client.Driver.FindActorByNetGuid((uint)pawns[0].ActorId) as SBasePawn;
                Assert.NotNull(copy);
                long tick = client.RenderTimeFixed / NetTimeline.Fraction;
                float alpha = (client.RenderTimeFixed % NetTimeline.Fraction) / (float)NetTimeline.Fraction;
                if (tick < 1 || tick + 1 >= rig.Server.World.TickCount) continue;
                float expectedPosition = 100f + tick + alpha;
                float motionError = Math.Abs(copy.Location.X - expectedPosition);
                if (motionError > worstMotion + 20f)
                    _output.WriteLine($"n={n} client={c} x={copy.Location.X:F2} time={tick}+{alpha:F2} newest={client.PawnStates!.NewestTick(copy)} snapshots={client.PawnStates.SnapshotsReceived} descriptors={client.PawnStates.AnimationRecords} missing={client.PawnStates.MissingDescriptors}");
                worstMotion = Math.Max(worstMotion, motionError);
                Assert.True(copy.Location.X >= previousPositions[c], "constant-speed presentation moved backwards");
                previousPositions[c] = copy.Location.X;
                float phaseStep = copy.PawnAnim[0].Frame - previousPhases[c];
                if (previousSamples[c] == n - 1)
                    Assert.False(phaseStep < -0.01f && phaseStep > -0.5f, "held speed-scaled animation moved backwards");
                previousPhases[c] = copy.PawnAnim[0].Frame; previousSamples[c] = n;
                float a = phases[tick], b = phases[tick + 1];
                if (b < a - 0.5f) b += 1f;
                float expectedPhase = a + (b - a) * alpha;
                float error = Math.Abs(copy.PawnAnim[0].Frame - expectedPhase);
                error = Math.Min(error, Math.Abs(error - 1f));
                // During an underflow presentation deliberately holds after 100 ms of extrapolation.
                // The phase agreement requirement applies when authoritative samples bracket display time.
                if (client.PawnStates!.NewestTick(copy) * NetTimeline.Fraction >= client.RenderTimeFixed)
                {
                    worstPhase = Math.Max(worstPhase, error);
                    bufferedSamples[c]++;
                }
                Assert.Equal(pawns[0].PawnAnim[0].Sequence, copy.PawnAnim[0].Sequence);
                samples[c]++;
            }
        }
        _output.WriteLine($"{profile}/{link}: position {worstMotion:F3} uu, phase {worstPhase:F5}, samples {string.Join(',', samples)}");
        Assert.All(samples, count => Assert.True(count > 500));
        Assert.All(bufferedSamples, count => Assert.True(count > 500));
        Assert.True(worstMotion <= 2f, $"position error {worstMotion} uu");
        Assert.True(worstPhase <= 0.02f, $"phase error {worstPhase}");
        Assert.All(rig.Clients, client => Assert.True(client.PawnStates!.SnapshotsReceived > 100));
        Assert.InRange(rig.SimLink!.MaximumDatagramBytes, 1, rig.Server.Driver.MaxPacketBytes);
    }
}
