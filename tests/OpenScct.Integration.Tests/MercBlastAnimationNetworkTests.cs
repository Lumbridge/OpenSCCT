using System;
using System.Collections.Generic;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MercBlastAnimationNetworkTests(ITestOutputHelper output)
{
    private static bool IsBlastReaction(Name clip) => clip.ToString().StartsWith("Blst", StringComparison.Ordinal) ||
        clip == new Name("LandStXxFd2") || clip == new Name("WakeStSpFd2");

    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    private sealed class PhysicsMeter(Actor target, IActorPhysics inner) : IActorPhysics
    {
        public float LargestStep;
        public int Steps;
        public readonly List<string> LargeSteps = new();
        public void performPhysics(Actor actor, float deltaSeconds)
        {
            if (ReferenceEquals(actor, target))
            {
                LargestStep = Math.Max(LargestStep, deltaSeconds);
                Steps++;
                if (deltaSeconds > Sim.DeltaTime + .001f && actor is SBasePawn pawn)
                    LargeSteps.Add($"{actor.XLevel.TickCount}:{deltaSeconds:F4}s {pawn.Controller?.GetStateName()}/{pawn.Physics} z={pawn.Location.Z:F4}");
            }
            inner.performPhysics(actor, deltaSeconds);
        }
    }

    private sealed class PhaseSteps
    {
        private Name _clip;
        private float _frame;
        private int _hold;
        public int Samples, Held, Doubled, MaxHold;
        public void Observe(PawnAnimChannel channel)
        {
            bool active = IsBlastReaction(channel.Sequence) &&
                channel.Frame >= 0 && channel.Rate > 0 && (channel.bLoop || channel.Frame < channel.AnimLast - .01f);
            if (active && _clip == channel.Sequence && _frame >= 0)
            {
                float change = channel.Frame - _frame;
                if (channel.bLoop && change < -.5f) change += 1;
                Samples++;
                if (MathF.Abs(change) < .00001f) { Held++; MaxHold = Math.Max(MaxHold, ++_hold); }
                else { _hold = 0; if (change > channel.Rate * Sim.DeltaTime * 1.5f) Doubled++; }
            }
            else _hold = 0;
            _clip = channel.Sequence; _frame = channel.Frame;
        }
        public override string ToString() => $"samples={Samples}/held={Held}/double={Doubled}/maxhold={MaxHold}";
    }

    private sealed class MotionSteps
    {
        private bool _wasFalling;
        private float _z;
        private int _held;
        public int Samples, MaxHold;
        public void Observe(SBasePawn pawn, float floor)
        {
            bool falling = pawn.Physics == EPhysics.PHYS_Falling;
            if (falling && _wasFalling && pawn.Location.Z > floor + 5 && MathF.Abs(pawn.Velocity.Z) > 25)
            {
                Samples++;
                _held = MathF.Abs(pawn.Location.Z - _z) < .00001f ? _held + 1 : 0;
                MaxHold = Math.Max(MaxHold, _held);
            }
            else _held = 0;
            _wasFalling = falling; _z = pawn.Location.Z;
        }
    }

    [Theory]
    [InlineData(ENetMode.NM_ListenServer, false, false)]
    [InlineData(ENetMode.NM_ListenServer, true, false)]
    [InlineData(ENetMode.NM_DedicatedServer, false, false)]
    [InlineData(ENetMode.NM_DedicatedServer, true, false)]
    [InlineData(ENetMode.NM_ListenServer, false, true)]
    [InlineData(ENetMode.NM_ListenServer, true, true)]
    public void RealFragBlastKeepsRemoteAnimationAdvancingThroughFlightAndLanding(ENetMode mode, bool lethal, bool dropOnset)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content is required.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 632, new SimLinkProfile("wifi35", 35, 20, 1f, .5f, .5f, 25));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes, NetMode = mode,
            Bots = [new("Spy", BotTeam.Spy, () => new IdleBrain()), new("Merc", BotTeam.Merc, () => new IdleBrain())],
            GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=9?LivesDEF=9",
        }, network.Bind, "server", 0);
        long frame = 0;
        SBasePawn? source = null;
        var history = new NetworkAnimChannel[5000];
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Server.Tick(clock.NowMicroseconds);
                if (source is not null) history[match.Server.World.TickCount] = NetworkAnimChannel.Capture(source.PawnAnim[0]);
                match.StepBots(clock.NowMicroseconds);
            }
        }
        Tick(180);
        if (mode == ENetMode.NM_ListenServer) match.ServerController(match.Bots[0])!.StartMatchOnServer();
        Tick(720);
        SPlayerController authority = match.ServerController(match.Bots[1])!;
        SPlayerController owner = match.Bots[1].Controller!;
        source = Assert.IsAssignableFrom<SPAWNDEFENSE>(authority.Pawn);
        SBasePawn local = owner.SBPawn!;
        source.SetLocation(new FVector(700, 300, source.CollisionHeight + 2));
        local.SetLocation(source.Location);
        source.Velocity = local.Velocity = FVector.Zero;
        match.Server.Movement.PeekEndpoint(authority)?.Sequenced?.ResetLifetime();
        Tick(60);
        source.Health = lethal ? 100 : 500;
        float floor = source.Location.Z;
        authority.fSpawnProtectionEnd = 0;
        ClientSession viewer = match.Bots[0].Session;
        SBasePawn remote = Assert.IsAssignableFrom<SPAWNDEFENSE>(viewer.Driver.FindActorByNetGuid((uint)source.ActorId));
        FVector at = source.Location - new FVector(100, 0, 0);
        SProjFrag grenade = source.XLevel.SpawnActor(new SProjFrag(), at, default, authority.Pawn, authority.Pawn)!;
        grenade.Explode(at, FVector.Zero);
        Assert.Equal(lethal, source.Health <= 0);
        var physics = new PhysicsMeter(source, source.XLevel.PhysicsDriver!);
        source.XLevel.PhysicsDriver = physics;
        var ownerClips = new HashSet<Name>();
        var remoteClips = new HashSet<Name>();
        var timeline = new List<string>();
        int wrongRun = 0, worstWrong = 0, frozenRun = 0, worstFrozen = 0, comparisons = 0, descriptorGaps = 0;
        float worstPhase = 0;
        Name previousClip = default;
        float previousFrame = -9;
        var sourceSteps = new PhaseSteps(); var ownerSteps = new PhaseSteps(); var remoteSteps = new PhaseSteps();
        var sourceMotion = new MotionSteps(); var remoteMotion = new MotionSteps();
        string? prior = null;
        for (int n = 0; n < 8 * Sim.Hz; n++)
        {
            // The owner starts the reaction first and sends its presentation back to the authority.
            // Drop the observer's link during that exchange, so the new descriptor itself is lost.
            if (dropOnset && n == 3) network.PauseLink("server", "Spy", clock.NowMicroseconds + 80_000);
            Tick(1);
            if (remote.bDeleteMe) break;
            sourceMotion.Observe(source, floor); remoteMotion.Observe(remote, floor);
            sourceSteps.Observe(source.PawnAnim[0]); ownerSteps.Observe(local.PawnAnim[0]); remoteSteps.Observe(remote.PawnAnim[0]);
            ownerClips.Add(local.PawnAnim[0].Sequence); remoteClips.Add(remote.PawnAnim[0].Sequence);
            int shown = (int)(viewer.RenderTimeFixed / NetTimeline.Fraction);
            if (shown < 1 || shown + 1 >= match.Server.World.TickCount) continue;
            float fraction = (viewer.RenderTimeFixed % NetTimeline.Fraction) / (float)NetTimeline.Fraction;
            NetworkAnimChannel expected = NetworkAnimChannel.Interpolate(history[shown], history[shown + 1], fraction);
            NetworkAnimChannel actual = NetworkAnimChannel.Capture(remote.PawnAnim[0]);
            bool blast = IsBlastReaction(expected.Sequence);
            bool buffered = viewer.PawnStates!.IsPoseBuffered(remote, viewer.RenderTimeFixed);
            bool moving = viewer.PawnStates.IsMotionBuffered(remote, viewer.RenderTimeFixed);
            if (blast && moving && !buffered) descriptorGaps++;
            wrongRun = blast && expected.Sequence != actual.Sequence ? wrongRun + 1 : 0;
            worstWrong = Math.Max(worstWrong, wrongRun);
            bool advancing = blast && actual.Frame >= 0 && (actual.Loop || actual.Frame < remote.PawnAnim[0].AnimLast - .01f);
            frozenRun = advancing && previousClip == actual.Sequence && MathF.Abs(previousFrame - actual.Frame) < .00001f ? frozenRun + 1 : 0;
            worstFrozen = Math.Max(worstFrozen, frozenRun);
            if (blast && buffered && expected.Sequence == actual.Sequence)
            {
                float error = MathF.Abs(expected.Frame - actual.Frame);
                if (expected.Loop) error = Math.Min(error, MathF.Abs(error - 1));
                worstPhase = Math.Max(worstPhase, error); comparisons++;
            }
            string now = $"{authority.GetStateName()}/{source.PawnAnim[0].Sequence} owner={owner.GetStateName()}/{local.PawnAnim[0].Sequence} remote={actual.Sequence} buffered={buffered}";
            if (now != prior) timeline.Add($"{n}: {now} f={actual.Frame:F3} expected={expected.Sequence}/{expected.Frame:F3} sourcef={source.PawnAnim[0].Frame:F3} ownerf={local.PawnAnim[0].Frame:F3}");
            prior = now; previousClip = actual.Sequence; previousFrame = actual.Frame;
        }
        string metrics = $"{mode} lethal={lethal} dropped={dropOnset}: wrong={worstWrong}ticks frozen={worstFrozen}ticks phase={worstPhase:F4} buffered={comparisons} gaps={descriptorGaps}; source({sourceSteps}) owner({ownerSteps}) remote({remoteSteps}); physicsSteps={physics.Steps} maxPhysicsStep={physics.LargestStep:F4}; sourceMotionHold={sourceMotion.MaxHold} remoteMotionHold={remoteMotion.MaxHold}\n" + string.Join('\n', timeline);
        output.WriteLine(metrics);
        if (physics.LargeSteps.Count > 0) output.WriteLine("Large physics steps: " + string.Join(';', physics.LargeSteps));
        Assert.Contains(new Name("BlstStAlDn2"), ownerClips);
        Assert.Contains(new Name("BlstStAlDn2"), remoteClips);
        Assert.Contains(new Name("LandStXxFd2"), remoteClips);
        Assert.True(comparisons > 5, metrics);
        Assert.True(worstWrong <= 1 && worstFrozen <= 3, metrics);
        if (lethal)
        {
            Assert.True(worstWrong == 0 && worstFrozen == 0 && sourceSteps.Doubled == 0, metrics);
            Assert.InRange(physics.LargestStep, Sim.DeltaTime - .00001f, Sim.DeltaTime + .00001f);
            Assert.True(sourceMotion.Samples > 10 && remoteMotion.Samples > 10, metrics);
            Assert.True(sourceMotion.MaxHold == 0 && remoteMotion.MaxHold == 0, metrics);
        }
        Assert.True(worstPhase < .03f, metrics);
        Assert.Equal(EClientState.Joined, viewer.State);
    }
}
