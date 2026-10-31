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
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class CooperativeLatencyTests
{
    private sealed class IdleBrain : BotBrain
    {
        private bool _stood;
        public override BotInput Think(BotContext context)
        {
            if (!_stood && context.Controller.Pawn is SPawnAttaque && context.Controller.IsInState("s_PlayerWalkingCr0H"))
            { _stood = true; return new BotInput { bCrouch = 1 }; }
            return BotInput.Idle;
        }
    }

    [Theory]
    [InlineData(NetProfile.Stock, "100", false)]
    [InlineData(NetProfile.Stock, "200", false)]
    [InlineData(NetProfile.Enhanced, "100", false)]
    [InlineData(NetProfile.Enhanced, "200", false)]
    [InlineData(NetProfile.Stock, "200", true)]
    [InlineData(NetProfile.Enhanced, "200", true)]
    public void TwoOwnersBoostThenHoistOnSteeEAndAllViewersRecoverThePairedClips(NetProfile profile, string link, bool immediate)
    {
        string folder = Assert.IsType<string>(WalkerRig.Maps.FindFolder("SteeE"));
        string chars = Assert.IsType<string>(PlayMapLoader.FindCharsDir(folder));
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 438, SimLinkProfile.Get(link));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = "SteeE", Maps = WalkerRig.Maps, Classes = WalkerRig.Classes, Profile = profile,
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
        }, network.Bind, "server", 0);
        long frame = 0;
        var clips = new HashSet<Name>[match.Bots.Count];
        for (int i = 0; i < clips.Length; i++) clips[i] = new();
        void Tick()
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz)); match.Step(clock.NowMicroseconds);
            for (int i = 0; i < match.Bots.Count; i++)
                foreach (SBasePawn pawn in match.Bots[i].Session.World.Level.DynamicActors<SBasePawn>())
                    if (pawn is SPawnAttaque) clips[i].Add(pawn.PawnAnim[0].Sequence);
        }
        void Wait(Func<bool> ready, int ticks, string stage)
        {
            for (int i = 0; i < ticks && !ready(); i++) Tick();
            Assert.True(ready(), $"{profile}/{link}: {stage}; " +
                $"climber {match.Bots[0].Controller?.GetStateName()} / {match.ServerController(match.Bots[0])?.GetStateName()}, " +
                $"helper {match.Bots[1].Controller?.GetStateName()} / {match.ServerController(match.Bots[1])?.GetStateName()}");
        }
        for (int i = 0; i < 900; i++) Tick();
        var climber = Assert.IsType<SPlayerController>(match.Bots[0].Controller);
        var helper = Assert.IsType<SPlayerController>(match.Bots[1].Controller);
        var sourceClimber = Assert.IsType<SPlayerController>(match.ServerController(match.Bots[0]));
        var sourceHelper = Assert.IsType<SPlayerController>(match.ServerController(match.Bots[1]));
        var animations = PawnAnimationSource.FromContent(chars);
        void UseClips(Level level) => level.SetService(new MovementService(animations, level.GetService<MovementService>()?.Hooks));
        UseClips(match.Server.World.Level);
        foreach (BotClient bot in match.Bots) UseClips(bot.Session.World.Level);
        void Place(SPlayerController pc, float y, int yaw)
        {
            var pawn = (SBasePawn)pc.Pawn!;
            Assert.False(pawn.bIsCrouched);
            Assert.True(MovementNatives.Driver(pawn)!.FarMoveActor(pawn, new FVector(-3340, y, -168 + 89.5f)));
            pawn.Velocity = pawn.Acceleration = default; pawn.Rotation = new FRotator(0, yaw, 0);
            pc.SetViewRotation(pawn.Rotation);
        }
        Place(climber, 2301, 16384); Place(sourceClimber, 2301, 16384);
        Place(helper, 2451, -16384); Place(sourceHelper, 2451, -16384);
        foreach (SPlayerController pc in new[] { sourceClimber, sourceHelper })
        {
            ((SBasePawn)pc.Pawn!).ResetNetworkHistory();
            match.Server.Movement.PeekEndpoint(pc)!.Sequenced!.ResetLifetime();
        }
        for (int i = 0; i < 180; i++) Tick();
        helper.eventPerformCoopMvt();
        Wait(() => ((SBasePawn)helper.Pawn!).bReadyForShortLadder && ((SBasePawn)sourceHelper.Pawn!).bReadyForShortLadder, 600, "ready to boost");
        Wait(() => match.Bots[0].Session.Driver.FindActorByNetGuid((uint)sourceHelper.Pawn!.ActorId) is SBasePawn { bReadyForShortLadder: true }, 120, "visible boost helper ready");
        Assert.Equal(SPlayerController.CA_ShortLadder, climber.CheckIfCanInteractWithMate(out _));
        Assert.True(climber.DoCoopInteraction(), "owner starts the boost approach");
        Wait(() => climber.IsInState(MovementStateNames.OnLedge) && sourceClimber.IsInState(MovementStateNames.OnLedge), 900, "boosted ledge hang");
        Wait(() => helper.IsInState(MovementStateNames.s_PlayerWalkingSt0H), 300, "helper finished boost");
        if (!immediate) for (int i = 0; i < 60; i++) Tick();
        climber.eventPerformCoopMvt();
        Wait(() => ((SBasePawn)climber.Pawn!).bReadyForHoistClimb && ((SBasePawn)sourceClimber.Pawn!).bReadyForHoistClimb, 300, "ready to hoist");
        Wait(() => match.Bots[1].Session.Driver.FindActorByNetGuid((uint)sourceClimber.Pawn!.ActorId) is SBasePawn { bReadyForHoistClimb: true }, 120, "visible hoist helper ready");
        Assert.Equal(SPlayerController.CA_HoistClimb, helper.CheckIfCanInteractWithMate(out _));
        Assert.True(helper.DoCoopInteraction(), "owner starts the hoist approach");
        Wait(() => helper.IsInState(MovementStateNames.s_PlayerWalkingCr0H) && sourceHelper.IsInState(MovementStateNames.s_PlayerWalkingCr0H), 1200, "hoisted to the top");
        for (int i = 0; i < 120; i++) Tick();
        Assert.True(climber.IsInState(MovementStateNames.OnLedge));
        Assert.True(sourceClimber.IsInState(MovementStateNames.OnLedge));
        Assert.InRange(helper.Pawn!.Location.Z - helper.Pawn.CollisionHeight, 341f, 347f);
        foreach ((SPlayerController own, SPlayerController source) in new[] { (climber, sourceClimber), (helper, sourceHelper) })
        {
            Assert.True((own.Pawn!.Location - source.Pawn!.Location).Size() <= 2f, $"{own.Pawn.Location} / {source.Pawn.Location}");
            Assert.Equal(0, match.Server.Movement.PeekEndpoint(source)!.Server!.Sent.Reinits);
        }
        foreach (BotClient bot in match.Bots)
        {
            Assert.Equal(EClientState.Joined, bot.Session.State);
            Assert.Equal(0, bot.Session.Movement.PeekEndpoint(bot.Controller!)!.Sequenced!.ResynchronizationRequests);
        }
        foreach (HashSet<Name> shown in clips)
            foreach (string clip in new[] { "CoopBtwAlHpEdS", "CoopCrAlHpEdP" })
                Assert.Contains(new Name(clip), shown);
    }
}
