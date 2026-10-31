using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.Combat;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class PairedActionLatencyTests
{
    [Theory]
    [InlineData(NetProfile.Stock, "100", 0)]
    [InlineData(NetProfile.Stock, "200", 0)]
    [InlineData(NetProfile.Enhanced, "100", 0)]
    [InlineData(NetProfile.Enhanced, "200", 0)]
    [InlineData(NetProfile.Stock, "100", 2)]
    [InlineData(NetProfile.Stock, "200", 2)]
    [InlineData(NetProfile.Enhanced, "100", 2)]
    [InlineData(NetProfile.Enhanced, "200", 2)]
    public void LethalDamageDeathPoseAndRespawnRemainAuthoritativeAcrossPawnLifetimes(NetProfile profile, string link, int victimIndex)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 420, SimLinkProfile.Get(link));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain { Stand = true }, _ => new IdleBrain()),
            Profile = profile,
        }, network.Bind, "server", 0);
        long frame = 0;
        void Tick()
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz)); match.Step(clock.NowMicroseconds);
        }
        for (int i = 0; i < 900; i++) Tick();
        BotClient victim = match.Bots[victimIndex];
        SPlayerController authority = match.ServerController(victim)!;
        SPlayerController killer = match.ServerController(match.Bots[victimIndex == 0 ? 2 : 0])!;
        var body = Assert.IsAssignableFrom<SBasePawn>(authority.Pawn);
        var deathClips = new System.Collections.Generic.HashSet<Name>(body.SPawnType == PawnType.SPAWNDEFENSE
            ? [body.ADeathStForward2H, body.ADeathStBack2H, body.ADeathStLeft2H, body.ADeathStRight2H, body.ADeathStDown2H]
            : [body.ADeathStForward, body.ADeathStBack, body.ADeathStLeft, body.ADeathStRight, body.ADeathStDown]);
        string chars = Assert.IsType<string>(PlayMapLoader.FindCharsDir(WalkerRig.Maps.FindFolder("SteeE")!));
        PawnAnimationSource animations = PawnAnimationSource.FromContent(chars);
        foreach (Name clip in deathClips)
            Assert.True(animations.TryGetClip(body, clip, out PawnClip authored) && authored.Frames > 2,
                $"Death parity requires the converted clip {clip}");
        void UseClips(Level level) => level.SetService(new MovementService(animations, level.GetService<MovementService>()?.Hooks));
        UseClips(match.Server.World.Level);
        foreach (BotClient client in match.Bots) UseClips(client.Session.World.Level);
        int deaths = authority.PlayerReplicationInfo!.Deaths, kills = killer.PlayerReplicationInfo!.Kills;
        var poses = new NetworkAnimChannel[4000];
        var health = new int[4000];
        var hidden = new bool[4000];
        var attachments = new NetworkPawnAttachments[4000];
        var comparedClips = new System.Collections.Generic.HashSet<Name>();
        var ownerClips = new System.Collections.Generic.HashSet<Name>();
        int compared = 0;
        bool hit = false;
        match.Server.BeforeWorldTick += _ =>
        {
            if (hit) return;
            hit = true;
            authority.PawnTakeDamage(200, killer.Pawn, body.Location, new FVector(-1, 0, 0), SDamageType.SDTBase, 1, 0);
        };
        for (int i = 0; i < 1800; i++)
        {
            Tick();
            int tick = (int)match.Server.World.TickCount;
            poses[tick] = NetworkAnimChannel.Capture(body.PawnAnim[0]);
            health[tick] = body.Health; hidden[tick] = body.bHidden;
            attachments[tick] = NetworkPawnAttachments.Capture(body);
            if (victim.Controller?.Pawn is SBasePawn owned) ownerClips.Add(owned.PawnAnim[0].Sequence);
            foreach (BotClient viewer in match.Bots)
            {
                long displayed = viewer.Session.RenderTimeFixed, shownTick = displayed / NetTimeline.Fraction;
                if (ReferenceEquals(viewer, victim) || shownTick < 902 || shownTick + 1 >= tick ||
                    viewer.Session.Driver.FindActorByNetGuid((uint)body.ActorId) is not SBasePawn { bDeleteMe: false } shown ||
                    !viewer.Session.PawnStates!.IsPoseBuffered(shown, displayed)) continue;
                NetworkAnimChannel a = poses[shownTick], b = poses[shownTick + 1];
                if (!a.SamePlay(b) || a.Sequence.IsNone) continue;
                NetworkAnimChannel expected = NetworkAnimChannel.Interpolate(a, b,
                    (displayed % NetTimeline.Fraction) / (float)NetTimeline.Fraction);
                Assert.Equal(expected.Sequence, shown.PawnAnim[0].Sequence);
                float tolerance = System.MathF.Max(System.MathF.Abs(a.Rate), System.MathF.Abs(b.Rate)) * Sim.DeltaTime + 0.001f;
                Assert.InRange(System.MathF.Abs(shown.PawnAnim[0].Frame - expected.Frame), 0, tolerance);
                Assert.Equal(health[shownTick], shown.Health);
                Assert.Equal(hidden[shownTick], shown.bHidden);
                Assert.Equal(attachments[shownTick], NetworkPawnAttachments.For(shown));
                comparedClips.Add(expected.Sequence); compared++;
            }
        }
        Assert.Equal(deaths + 1, authority.PlayerReplicationInfo.Deaths);
        Assert.Equal(kills + 1, killer.PlayerReplicationInfo.Kills);
        Assert.True(compared > 300, $"only {compared} buffered death samples");
        Assert.Contains(comparedClips, deathClips.Contains);
        Assert.Contains(ownerClips, deathClips.Contains);
        Assert.NotSame(body, authority.Pawn);
        var live = Assert.IsAssignableFrom<SBasePawn>(authority.Pawn);
        var ownedLive = Assert.IsAssignableFrom<SBasePawn>(victim.Controller!.Pawn);
        Assert.True(live.Health > 0); Assert.True(ownedLive.Health > 0);
        Assert.InRange((live.Location - ownedLive.Location).Size(), 0, 2);
        Assert.Equal(deaths + 1, victim.Controller.PlayerReplicationInfo!.Deaths);
        foreach (BotClient owner in match.Bots)
        {
            Assert.Equal(EClientState.Joined, owner.Session.State);
            var transport = owner.Session.Movement.PeekEndpoint(owner.Controller!)!.Sequenced!;
            Assert.True(transport.ResynchronizationRequests == 0, $"{owner.Name}: {transport.FirstResynchronization}");
        }
    }

    private sealed class IdleBrain : BotBrain
    {
        public bool Stand;
        private bool _pressed;
        public override BotInput Think(BotContext context)
        {
            if (Stand && !_pressed && context.Controller.IsInState("s_PlayerWalkingCr0H"))
            {
                _pressed = true;
                return new BotInput { bCrouch = 1 };
            }
            return BotInput.Idle;
        }
    }

    [Theory]
    [InlineData(NetProfile.Stock, "100")]
    [InlineData(NetProfile.Stock, "200")]
    [InlineData(NetProfile.Enhanced, "100")]
    [InlineData(NetProfile.Enhanced, "200")]
    public void TwoOwnersCompleteAGrabKnockoutAndRecoveryWithoutDuplicateDamageOrPredictionResets(NetProfile profile, string link)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 208, SimLinkProfile.Get(link));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = BotMatchSettings.TwoVersusTwo(i => new IdleBrain { Stand = i == 0 }, _ => new IdleBrain()),
            Profile = profile,
        }, network.Bind, "server", 0);
        SBasePawn? recordedSpy = null, recordedMerc = null;
        var spyHistory = new NetworkAnimChannel[3600];
        var mercHistory = new NetworkAnimChannel[3600];
        int compared = 0;
        var comparedClips = new System.Collections.Generic.HashSet<Name>();
        var sourceClips = new System.Collections.Generic.HashSet<Name>();
        var ownerClips = new System.Collections.Generic.HashSet<Name>();
        void Compare(BotClient viewer, SBasePawn source, NetworkAnimChannel[] history)
        {
            long displayed = viewer.Session.RenderTimeFixed;
            long tick = displayed / NetTimeline.Fraction;
            if (tick < 1025 || tick + 1 >= match.Server.World.TickCount ||
                viewer.Session.Driver.FindActorByNetGuid((uint)source.ActorId) is not SBasePawn { NetworkPoseReady: true } shown ||
                !viewer.Session.PawnStates!.IsPoseBuffered(shown, displayed)) return;
            NetworkAnimChannel a = history[tick], b = history[tick + 1];
            if (!a.SamePlay(b)) return; // the next tick is an exact authored boundary, not an interpolation span
            float alpha = (displayed % NetTimeline.Fraction) / (float)NetTimeline.Fraction;
            NetworkAnimChannel expected = NetworkAnimChannel.Interpolate(a, b, alpha);
            Assert.Equal(expected.Sequence, shown.PawnAnim[0].Sequence);
            float error = System.MathF.Abs(expected.Frame - shown.PawnAnim[0].Frame);
            if (expected.Loop && expected.Frame >= 0 && shown.PawnAnim[0].Frame >= 0)
                error = System.MathF.Min(error, System.MathF.Abs(error - 1f));
            PawnClip clip = PawnActions.Clip(source, expected.Sequence);
            float authoredRate = clip.Frames > 0 ? clip.Fps / clip.Frames : 0;
            float allowance = System.MathF.Max(authoredRate, System.MathF.Max(System.MathF.Abs(a.Rate), System.MathF.Abs(b.Rate))) * Sim.DeltaTime + 0.001f;
            Assert.True(error <= allowance, $"{profile}/{link} displayed {tick}+{alpha:F2} {expected.Sequence}: phase {shown.PawnAnim[0].Frame:F4}, expected {expected.Frame:F4}, tolerance {allowance:F4}");
            compared++;
            comparedClips.Add(expected.Sequence);
        }
        long frame = 0;
        void Tick()
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz)); match.Step(clock.NowMicroseconds);
            if (recordedSpy is null || recordedMerc is null) return;
            int tick = (int)match.Server.World.TickCount;
            spyHistory[tick] = NetworkAnimChannel.Capture(recordedSpy.PawnAnim[0]);
            mercHistory[tick] = NetworkAnimChannel.Capture(recordedMerc.PawnAnim[0]);
            sourceClips.Add(recordedMerc.PawnAnim[0].Sequence);
            if (match.Bots[2].Controller?.Pawn is SBasePawn ownMerc) ownerClips.Add(ownMerc.PawnAnim[0].Sequence);
            foreach (BotClient viewer in match.Bots)
            {
                if (viewer == match.Bots[0] || viewer == match.Bots[2]) continue;
                Compare(viewer, recordedSpy, spyHistory); Compare(viewer, recordedMerc, mercHistory);
            }
        }
        for (int i = 0; i < 900; i++) Tick();
        BotClient spy = match.Bots[0], merc = match.Bots[2];
        SPlayerController spyOwner = Assert.IsType<SPlayerController>(spy.Controller);
        SPlayerController mercOwner = Assert.IsType<SPlayerController>(merc.Controller);
        SPlayerController spyAuthority = Assert.IsType<SPlayerController>(match.ServerController(spy));
        SPlayerController mercAuthority = Assert.IsType<SPlayerController>(match.ServerController(merc));
        var sourceSpy = Assert.IsAssignableFrom<SBasePawn>(spyAuthority.Pawn);
        var sourceMerc = Assert.IsAssignableFrom<SBasePawn>(mercAuthority.Pawn);
        recordedSpy = sourceSpy; recordedMerc = sourceMerc;
        var localSpy = Assert.IsAssignableFrom<SBasePawn>(spyOwner.Pawn);
        var localMerc = Assert.IsAssignableFrom<SBasePawn>(mercOwner.Pawn);
        string chars = Assert.IsType<string>(PlayMapLoader.FindCharsDir(WalkerRig.Maps.FindFolder("SteeE")!));
        PawnAnimationSource animations = PawnAnimationSource.FromContent(chars);
        Assert.True(animations.TryGetClip(sourceMerc, new Name("GrbdStAlWkA"), out PawnClip wake) && wake.Frames > 2,
            "Paired-action parity requires the converted character clips, not two-frame stand-ins.");
        void UseClips(Level level)
        {
            MovementService? previous = level.GetService<MovementService>();
            level.SetService(new MovementService(animations, previous?.Hooks));
        }
        UseClips(match.Server.World.Level);
        foreach (BotClient client in match.Bots) UseClips(client.Session.World.Level);
        Assert.False(localSpy.bIsCrouched);
        FRotator facing = sourceMerc.Rotation;
        facing.Pitch = facing.Roll = 0;
        FVector mercAt = sourceMerc.Location;
        FVector spyAt = mercAt - facing.Vector() * 85;
        spyAt.Z += localSpy.CollisionHeight - localMerc.CollisionHeight;
        void Place(SBasePawn pawn, FVector at)
        {
            Assert.True(MovementNatives.Driver(pawn)!.FarMoveActor(pawn, at));
            pawn.Rotation = facing; pawn.Velocity = pawn.Acceleration = default;
            ((SPlayerController)pawn.Controller!).SetViewRotation(facing);
        }
        Place(sourceSpy, spyAt); Place(localSpy, spyAt);
        Place(sourceMerc, mercAt); Place(localMerc, mercAt);
        sourceSpy.ResetNetworkHistory(); sourceMerc.ResetNetworkHistory();
        match.Server.Movement.PeekEndpoint(spyAuthority)!.Sequenced!.ResetLifetime();
        match.Server.Movement.PeekEndpoint(mercAuthority)!.Sequenced!.ResetLifetime();
        sourceMerc.iLastWakeTime = -100;
        mercAuthority.TimeShockForGrab = -100;
        if (spyAuthority.GameReplicationInfo is { } gri) gri.bAvlb_SPY_Grab = true;
        for (int i = 0; i < 120; i++) Tick();
        var target = Assert.IsAssignableFrom<SBasePawn>(spy.Session.Driver.FindActorByNetGuid((uint)sourceMerc.ActorId));
        Assert.True(spyOwner.CanGrabPawn(target), $"spy {localSpy.Location}, merc {target.Location}, yaw {localSpy.Rotation.Yaw}/{target.Rotation.Yaw}");
        Assert.True(spyAuthority.CanGrabPawn(sourceMerc), $"authority reach: spy {sourceSpy.Location}/{sourceSpy.Rotation.Yaw}, merc {sourceMerc.Location}/{sourceMerc.Rotation.Yaw}");
        Assert.True(CombatStateEvents.CanBeGrabbed(mercAuthority));
        Assert.True(sourceMerc.bCanBeGrabbed);
        Assert.True(spyOwner.AskToGrabPawn(), $"owner geometry {localSpy.Location}/{target.Location}, allowed {spyOwner.GameReplicationInfo?.bAvlb_SPY_Grab}, grabbable {target.bCanBeGrabbed}, stand {localSpy.CanUnCrouch()}");
        for (int i = 0; i < 360 && !spyOwner.IsInState("s_GrabbingPlayer"); i++) Tick();
        Assert.True(spyOwner.IsInState("s_GrabbingPlayer"), $"owner {spyOwner.GetStateName()}, authority {spyAuthority.GetStateName()}, target {mercAuthority.GetStateName()}");
        Assert.Same(sourceMerc, sourceSpy.m_AttackTarget);
        Assert.Same(sourceSpy, sourceMerc.m_AttackedBy);
        for (int i = 0; i < 30; i++) Tick();
        foreach (BotClient viewer in match.Bots)
        {
            if (viewer == spy || viewer == merc) continue;
            var shownSpy = Assert.IsAssignableFrom<SBasePawn>(viewer.Session.Driver.FindActorByNetGuid((uint)sourceSpy.ActorId));
            var shownMerc = Assert.IsAssignableFrom<SBasePawn>(viewer.Session.Driver.FindActorByNetGuid((uint)sourceMerc.ActorId));
            Assert.Same(shownMerc, shownSpy.m_AttackTarget);
            Assert.Same(shownSpy, shownMerc.m_AttackedBy);
            Assert.False(shownSpy.PawnAnim[0].Sequence.IsNone);
            Assert.False(shownMerc.PawnAnim[0].Sequence.IsNone);
        }
        int health = sourceMerc.Health;
        Assert.True(spyOwner.CheckForRoomToFreeGrabbedGuy(GrabReleaseMethode_.KO));
        bool sawSleeping = false;
        for (int i = 0; i < 1200; i++)
        {
            Tick();
            sawSleeping |= sourceMerc.bSleeping || sourceMerc.bLyingOnGround;
        }
        Assert.Equal(health - 51, sourceMerc.Health);
        Assert.True(sawSleeping);
        string recovery = $"recovery owner {mercOwner.GetStateName()} {localMerc.PawnAnim[0].Sequence}/{localMerc.PawnAnim[0].Frame}, authority {mercAuthority.GetStateName()} {sourceMerc.PawnAnim[0].Sequence}/{sourceMerc.PawnAnim[0].Frame}, grabbed {sourceMerc.m_AttackedBy}, spy {spyOwner.GetStateName()}/{spyAuthority.GetStateName()}";
        Assert.False(sourceMerc.bSleeping, recovery);
        Assert.False(sourceMerc.bLyingOnGround, recovery);
        Assert.True((sourceMerc.Location - localMerc.Location).Size() <= 2,
            $"merc {mercOwner.GetStateName()}/{mercAuthority.GetStateName()} {localMerc.Location}/{sourceMerc.Location}");
        Assert.True((sourceSpy.Location - localSpy.Location).Size() <= 2);
        Assert.True(compared > 1000, $"only {compared} historical paired-action poses compared");
        Assert.True(merc.Session.Movement.OwnerActionsApplied > 0);
        Assert.Contains(new Name("GrbdStAlKo0"), ownerClips);
        Assert.Contains(new Name("GrbdStAlWkA"), ownerClips);
        foreach (string clip in new[] { "GrabStAlBg0", "GrbdStAlBg0", "GrabStAlKo0", "GrbdStAlKo0", "GrbdStAlWkA" })
            Assert.True(comparedClips.Contains(new Name(clip)), $"missing {clip}: compared {string.Join(',', comparedClips)}; source {string.Join(',', sourceClips)}; owner {string.Join(',', ownerClips)}");
        foreach (BotClient owner in match.Bots)
        {
            Assert.Equal(EClientState.Joined, owner.Session.State);
            Assert.Equal(0, owner.Session.Movement.PeekEndpoint(owner.Controller!)!.Sequenced!.ResynchronizationRequests);
            Assert.Equal(0, match.Server.Movement.PeekEndpoint(match.ServerController(owner)!)!.Server!.Sent.Reinits);
        }
    }
}
