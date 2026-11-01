using System;
using System.Collections.Generic;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class LedgeKillNetworkTests(ITestOutputHelper output)
{
    private sealed class InputBrain : BotBrain
    {
        private bool _stood;
        public bool Use;
        public int Yaw = 16384;
        public override BotInput Think(BotContext context)
        {
            bool stand = !_stood && context.Pawn.bIsCrouched && context.Controller.IsInState("s_PlayerWalkingCr0H");
            _stood |= stand;
            return new BotInput { View = new FRotator(0, Yaw, 0), FastSpeed = true,
                bCrouch = (byte)(stand ? 1 : 0), bUsing = (byte)(Use ? 1 : 0) };
        }
    }

    [Theory]
    [InlineData(NetProfile.Enhanced, "A", 0)]
    [InlineData(NetProfile.Enhanced, "200", 0)]
    [InlineData(NetProfile.Stock, "200", 0)]
    [InlineData(NetProfile.Enhanced, "200", 600)]
    public void HangingSpyPullsAMercThroughNormalUseAndAllPeersFinishTheKill(NetProfile profile, string link, int serverLeadTicks)
    {
        string? folder = WalkerRig.Maps.FindFolder("SteeE");
        Assert.SkipWhen(folder is null, "SteeE content is required.");
        string? chars = PlayMapLoader.FindCharsDir(folder!);
        Assert.SkipWhen(chars is null, "Authored character animations are required.");
        var animations = PawnAnimationSource.FromContent(chars!);
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 5517, SimLinkProfile.Get(link));
        var spyInput = new InputBrain();
        var mercInput = new InputBrain { Yaw = 49152 };
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = "SteeE", Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            NetMode = ENetMode.NM_ListenServer,
            Profile = profile,
            GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=2?LivesDEF=2?TimesRespawnsDEF=10",
            Bots = [new("Spy", BotTeam.Spy, () => spyInput), new("Merc", BotTeam.Merc, () => mercInput)],
        }, network.Bind, "server", 0);
        long frame = 0;
        var states = new HashSet<string>();
        var tracked = new Dictionary<string, SBasePawn>();
        var clips = new Dictionary<string, HashSet<Name>>();
        SBasePawn? approach = null;
        float longestReposStep = 0, longestRootStep = 0;
        long firstDeathFrame = 0;
        void Tick(int count = 1)
        {
            for (int n = 0; n < count; n++)
            {
                FVector previous = approach?.Location ?? default;
                bool repositioning = approach?.Physics == EPhysics.PHYS_Repositionnal;
                bool pulling = approach is { Physics: EPhysics.PHYS_RootMotion } &&
                    approach.PawnAnim[0].Sequence == new Name("GrbdStLgRail") && approach.PawnAnim[0].Frame is > 0.05f and < 0.9f;
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Step(clock.NowMicroseconds);
                if (repositioning && approach is not null)
                    longestReposStep = MathF.Max(longestReposStep, (approach.Location - previous).Size2D());
                if (pulling && approach is not null)
                    longestRootStep = MathF.Max(longestRootStep, (approach.Location - previous).Size());
                if (approach is { Physics: EPhysics.PHYS_Repositionnal or EPhysics.PHYS_RootMotion })
                    Assert.True(approach.fLastRootMotionTimeCalled <= approach.XLevel.WorldTimeSeconds + Sim.DeltaTime + 0.001f,
                        $"root clock ran ahead: {approach.fLastRootMotionTimeCalled} vs {approach.XLevel.WorldTimeSeconds}");
                if (approach is { Health: <= 0 })
                {
                    if (firstDeathFrame == 0) firstDeathFrame = frame;
                    float landingSeconds = PawnActions.Clip(approach, new Name("LandStXxFd2")).OneShotSeconds;
                    if (frame - firstDeathFrame < landingSeconds * Sim.Hz)
                        Assert.False(approach.bDeleteMe, "corpse removed before the authored landing animation finished");
                }
                foreach (BotClient bot in match.Bots)
                    if (bot.Controller is { } pc)
                    {
                        if (pc.StateNode is SPlayerController.s_Dead)
                        {
                            Assert.True(pc.bBehindView);
                            Assert.False(pc.bFirstPersonWithPawn, "the completed fall restored first-person view after death detached the head camera");
                        }
                        pc.Level.m_dT = Sim.DeltaTime;
                        PlayerCalcView.FrameView(pc, out _, out _, out _);
                    }
                string state = $"{match.Bots[0].Controller?.GetStateName()}/{match.Bots[1].Controller?.GetStateName()}";
                if (states.Add(state)) output.WriteLine($"{frame}: {state}");
                foreach (var (label, pawn) in tracked) clips[label].Add(pawn.PawnAnim[0].Sequence);
            }
        }
        for (int i = 0; i < serverLeadTicks; i++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
            match.Server.Tick(clock.NowMicroseconds);
        }
        Tick(120);
        match.ServerController(match.Bots[0])!.StartMatchOnServer();
        Tick(780);
        void Install(Level level) => level.SetService(new MovementService(animations, level.GetService<MovementService>()?.Hooks));
        Install(match.Server.World.Level);
        foreach (BotClient bot in match.Bots) Install(bot.Session.World.Level);
        SPlayerController spy = match.Bots[0].Controller!, merc = match.Bots[1].Controller!;
        SPlayerController sourceSpy = match.ServerController(match.Bots[0])!, sourceMerc = match.ServerController(match.Bots[1])!;
        // Headless BotClient has no PlaySession.RestoreBehindView; reproduce that normal viewport setup.
        spy.bBehindView = spy.Pawn!.PointOfView();
        merc.bBehindView = merc.Pawn!.PointOfView();
        void Place(SPlayerController pc, FVector floor, int yaw)
        {
            var pawn = (SBasePawn)pc.Pawn!;
            Assert.False(pawn.bIsCrouched);
            Assert.True(MovementNatives.Driver(pawn)!.FarMoveActor(pawn, floor + new FVector(0, 0, pawn.CollisionHeight + 2)));
            pawn.Velocity = pawn.Acceleration = default;
            pawn.Rotation = new FRotator(0, yaw, 0);
            pc.SetViewRotation(pawn.Rotation);
            pawn.ResetNetworkHistory();
        }
        foreach (SPlayerController pc in new[] { spy, sourceSpy }) Place(pc, new FVector(-2450, 1305, -1127.5f), 16384);
        foreach (SPlayerController pc in new[] { merc, sourceMerc }) Place(pc, new FVector(-2450, 1410, -807.5f), 49152);
        Tick(120);
        output.WriteLine($"placed spy={spy.Pawn!.Location} merc={merc.Pawn!.Location}");
        Tick(15);
        spy.eventJump();
        for (int n = 0; n < 180 && !spy.IsInState("OnLedge"); n++) Tick();
        Assert.True(spy.IsInState("OnLedge"), $"normal jump failed to hang: {spy.GetStateName()} {spy.Pawn.Location}; source {sourceSpy.GetStateName()} {sourceSpy.Pawn!.Location}");
        Tick(60);
        var visibleMerc = (SBasePawn)match.Bots[0].Session.Driver.FindActorByNetGuid((uint)sourceMerc.Pawn!.ActorId)!;
        var visibleSpy = (SBasePawn)match.Bots[1].Session.Driver.FindActorByNetGuid((uint)sourceSpy.Pawn!.ActorId)!;
        tracked.Add("spy authority", sourceSpy.SBPawn!); tracked.Add("spy owner", spy.SBPawn!); tracked.Add("spy observer", visibleSpy);
        tracked.Add("merc authority", sourceMerc.SBPawn!); tracked.Add("merc owner", merc.SBPawn!); tracked.Add("merc observer", visibleMerc);
        foreach (string label in tracked.Keys) clips.Add(label, []);
        Assert.True(PawnActions.Clip(spy.SBPawn!, new Name("GrabLgBgRail")).Frames > 2);
        Assert.True(PawnActions.Clip(merc.SBPawn!, new Name("GrbdStLgRail")).Frames > 2);
        output.WriteLine($"hang {spy.Pawn.Location} ledge={((SBasePawn)spy.Pawn).ZipP1} pc={spy.Location} merc={visibleMerc.Location} geo={spy.bTestGrabLedgeGeo()}");
        Assert.True(spy.bTestGrabLedgeGeo(), "authored rail geometry does not permit a pull");
        Assert.True(SPlayerController.OnLedge.Instance.CanGrabPawn(spy, visibleMerc), "merc not in the original ledge grab reach");
        spyInput.Use = true;
        Tick(1);
        spyInput.Use = false;
        var body = (SBasePawn)sourceMerc.Pawn!;
        approach = body;
        for (int n = 0; n < 600 && body.Health > 0; n++)
        {
            Tick();
        }
        Tick(180);
        Assert.True(body.Health <= 0, $"merc survived normal ledge pull; spy={spy.GetStateName()} merc={merc.GetStateName()} states={string.Join(',', states)}");
        Assert.InRange(longestReposStep, 0, 180 * Sim.DeltaTime + 0.05f);
        PawnClip pull = PawnActions.Clip(body, new Name("GrbdStLgRail"));
        float fastestAuthoredStep = 0;
        for (int i = 0; i < pull.Frames - 1; i++)
            fastestAuthoredStep = MathF.Max(fastestAuthoredStep,
                (pull.RootAt((i + 1f) / pull.Frames) - pull.RootAt(i / (float)pull.Frames)).Size());
        Assert.InRange(longestRootStep, 0.001f, fastestAuthoredStep * pull.Fps * Sim.DeltaTime + 0.1f);
        output.WriteLine($"Maximum authority step: reposition {longestReposStep:F3}uu, paired root {longestRootStep:F3}uu.");
        Assert.True(merc.SBPawn!.Health <= 0);
        Assert.Contains("s_GrabbingFromLedge_Go/s_GrabbedFromLedge_Begin", states);
        Assert.True(spy.IsInState("OnLedge"), $"spy did not return to hang: {spy.GetStateName()}");
        Assert.Equal(1, sourceSpy.PlayerReplicationInfo!.Stats!.Spy_LedgeGrabs);
        Assert.Equal(1, sourceSpy.PlayerReplicationInfo.Kills);
        Assert.Equal(1, sourceMerc.PlayerReplicationInfo!.Deaths);
        Assert.Equal(1, sourceMerc.PlayerReplicationInfo.NumLives);
        Assert.Equal(1, sourceMerc.PlayerReplicationInfo.Stats!.Merc_DeathFromLedgeGrab);
        Assert.False(match.Server.Game!.bGameEnded);
        foreach (var (label, pawn) in tracked)
        {
            string[] expected = label.StartsWith("spy", StringComparison.Ordinal)
                ? ["GrabLgBgRail", "GrabLgEdRail", "LedgStAlNt0"]
                : ["GrbdStLgRail", "FallStAlFr0", "LandStXxFd2"];
            foreach (string clip in expected)
                Assert.True(clips[label].Contains(new Name(clip)), $"{label} missing {clip}: {string.Join(',', clips[label])}");
            if (label.StartsWith("merc", StringComparison.Ordinal))
            {
                Assert.True(pawn.Health <= 0, $"{label} survived");
                Assert.Equal(new Name("LandStXxFd2"), pawn.PawnAnim[0].Sequence);
                Assert.InRange((pawn.Location - body.Location).Size(), 0, 3);
            }
        }
        var ownerMoves = match.Bots[1].Session.Movement.PeekEndpoint(merc)!.Sequenced!;
        uint afterDeath = ownerMoves.NextSequence;
        Tick(120);
        Assert.Equal(afterDeath, ownerMoves.NextSequence); // Complete the lethal command, without new corpse heartbeats.
        Assert.Equal(1, sourceMerc.PlayerReplicationInfo.Deaths);
        foreach (BotClient bot in match.Bots)
        {
            Assert.Equal(EClientState.Joined, bot.Session.State);
            Assert.Equal(0, bot.Session.Movement.PeekEndpoint(bot.Controller!)!.Sequenced!.ResynchronizationRequests);
        }
    }
}
