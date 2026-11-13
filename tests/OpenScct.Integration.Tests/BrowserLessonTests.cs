using System;
using System.IO;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using PracticeRules = OpenScct.Game.Modes.PracticeRules;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class BrowserLessonTests
{
    [Fact]
    public void BrowserCatchUpLimitDropsStaleWorkAndResumesOrdinaryTicks()
    {
        using var rig = new Rig("vision-cancelling");
        float before = rig.Session.Host.World.Level.Info.TimeSeconds;
        rig.Clock.NowMicroseconds += 2_000_000;
        Assert.Equal(2, rig.Session.Update(maxCatchUpTicks: 2));
        Assert.InRange(rig.Session.Host.World.Level.Info.TimeSeconds - before, Sim.DeltaTime * 1.9f, Sim.DeltaTime * 2.1f);
        Assert.Equal(0, rig.Session.Update(maxCatchUpTicks: 2));

        rig.Clock.NowMicroseconds += 16_667;
        Assert.Equal(1, rig.Session.Update(maxCatchUpTicks: 2));
        Assert.NotNull(rig.Session.LocalPawn);
        Assert.Null(rig.Session.LoginError);

        before = rig.Session.Host.World.Level.Info.TimeSeconds;
        rig.Clock.NowMicroseconds += 2_000_000;
        Assert.Equal(PlaySession.MaxCatchUpTicks, rig.Session.Update());
        Assert.InRange(rig.Session.Host.World.Level.Info.TimeSeconds - before,
            Sim.DeltaTime * (PlaySession.MaxCatchUpTicks - .1f), Sim.DeltaTime * (PlaySession.MaxCatchUpTicks + .1f));
    }

    [Fact]
    public void EmptyPracticeBotControlCanWaitForTheFirstRequestedBotBeforeBuildingItsWorld()
    {
        using var rig = new Rig("vision-cancelling");
        using var bots = new OpenScct.Bots.ServerBots(rig.Session.Host, new OpenScct.Bots.ServerBotSettings
        {
            Bots = [], Maps = rig.Session.Catalog, PrepareWhenEmpty = false,
        });
        rig.Tick(60);
        Assert.Null(bots.World);
        var added = bots.Add(new OpenScct.Bots.ServerBotSpec("RequestedMerc", 1,
            OpenScct.Game.AI.AiDifficulty.For(OpenScct.Game.AI.AiSkill.Normal)));
        Assert.NotNull(bots.World);
        Assert.NotNull(added?.Pawn);
        Assert.Single(bots.Bots);
    }

    private sealed class Rig : IDisposable
    {
        public readonly StepClock Clock = new();
        public readonly PlaySession Session;
        public readonly BrowserLesson Lesson;

        public Rig(string kind)
        {
            string? content = Environment.GetEnvironmentVariable("SVM_TEST_CONTENT");
            string[] roots = content is null ? [.. MapCatalog.CreateDefault().Roots] : [Path.Combine(content, "svmap")];
            Assert.SkipWhen(new MapCatalog(roots).FindFolder("TestRange") is null, "TestRange content is required.");
            Session = new PlaySession(new PlaySettings
            {
                Map = "TestRange", Side = PlaySide.Spy, Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Inline, Bots = kind == "spy-grab" ? 1 : 0,
                StartAt = BrowserLesson.Start(kind),
                MapRoots = roots,
            }, Clock);
            Lesson = new BrowserLesson(Session, kind);
            for (int i = 0; i < 600 && !Lesson.Ready; i++) Tick();
            Assert.True(Lesson.Ready, Describe());
        }

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Session.Input.EndTick();
                Clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                Session.Step(Clock.NowMicroseconds);
            }
        }

        public void Dispose() { Lesson.Dispose(); Session.Dispose(); }
        public string Describe() => $"{Lesson.Feedback}; local={Session.LocalController?.GetStateName()} {Session.LocalPawn?.Location}; server={Session.ServerController?.GetStateName()} {Session.ServerController?.Pawn?.Location}; " +
            string.Join(';', Session.Bots?.Controllers.Select(pc => $"bot={pc.GetStateName()} {pc.Pawn?.Location} id={pc.Pawn?.ActorId}") ?? []) +
            $"; clientMercs={Session.Client.World.Level.AllActors<SPAWNDEFENSE>().Count()}";
    }

    [Fact]
    public void MovingMercenaryWalksWithoutAttackingAndCannotKillTheLearner()
    {
        using var rig = new Rig("spy-grab");
        var bot = Assert.Single(rig.Session.Bots!.Bots);
        float before = bot.Controller.Pawn!.Location.X;
        rig.Lesson.Action("moving");
        rig.Tick(600);
        Assert.NotEqual(before, bot.Controller.Pawn.Location.X);
        Assert.True(bot.Held);
        Assert.Equal(0, bot.Controller.bFire);
        Assert.Equal(0, bot.Controller.bAltFire);
        Assert.Equal(100, rig.Session.LocalPawn!.Health);
        Assert.True(PracticeRules.Of(rig.Session.ServerController!)!.IsTank(rig.Session.ServerController!));
    }

    [Fact]
    public void GrabButtonRunsTheRealGrabAndReadOnlyProbeReportsTheResult()
    {
        using var rig = new Rig("spy-grab");
        rig.Tick(300);
        var target = rig.Lesson.Target!;
        var position = target.Location - new FVector(100, 0, 0);
        Assert.True(rig.Session.ServerController!.Pawn!.SetLocation(position));
        Assert.True(rig.Session.LocalPawn!.SetLocation(position));
        rig.Session.LocalPawn.Rotation = default;
        rig.Session.LocalController!.SetViewRotation(default);
        rig.Tick(3);
        Assert.Equal("Grab available.", rig.Session.LocalController.DescribeGrab(target));
        rig.Lesson.Action("grab");
        rig.Tick(180);
        Assert.Same(target, rig.Session.LocalPawn.m_AttackTarget);
        Assert.True(rig.Lesson.Grabs > 0);
    }

    [Fact]
    public void VisionLessonFallsAndMeasuresTheNativeLandingRecovery()
    {
        using var rig = new Rig("vision-cancelling");
        rig.Tick(120);
        Assert.InRange(rig.Session.LocalPawn!.Location.Z, 640, 700);
        rig.Lesson.Action("drop");
        bool fell = false;
        for (int i = 0; i < 600 && rig.Lesson.RecoveryMs is null; i++)
        {
            rig.Tick();
            fell |= rig.Session.LocalPawn.Physics == EPhysics.PHYS_Falling;
        }
        Assert.True(fell, rig.Describe());
        Assert.NotNull(rig.Lesson.RecoveryMs);
        Assert.True(rig.Lesson.RecoveryMs > 0);
        Assert.InRange(rig.Session.LocalPawn.Location.Z, 40, 100);
        rig.Tick(120);
        Assert.Equal(0, rig.Session.Input.Get("BT_Up"));
    }

    [Fact]
    public void VisionSwitchDuringLandingShortensTheActualRecovery()
    {
        double Drop(bool switchVision)
        {
            using var rig = new Rig("vision-cancelling");
            rig.Tick(120);
            rig.Lesson.Action("drop");
            bool pressed = false;
            for (int i = 0; i < 600 && rig.Lesson.RecoveryMs is null; i++)
            {
                if (switchVision && !pressed && rig.Session.LocalPawn!.Location.Z < 150 && rig.Session.LocalController!.IsInState("PlayerLanding"))
                {
                    rig.Lesson.Action("night");
                    pressed = true;
                }
                rig.Tick();
            }
            if (switchVision) Assert.True(pressed);
            Assert.NotNull(rig.Lesson.RecoveryMs);
            return rig.Lesson.RecoveryMs.Value;
        }
        double baseline = Drop(false), cancelled = Drop(true);
        Assert.True(cancelled < baseline, $"Baseline {baseline:0} ms; switched {cancelled:0} ms.");
    }

    [Fact]
    public void ReturnPortalMovesBothCopiesToTheLedgeAndKeepsMeasuredResults()
    {
        using var rig = new Rig("vision-cancelling");
        rig.Tick(120);
        rig.Lesson.Action("drop");
        for (int i = 0; i < 600 && rig.Lesson.RecoveryMs is null; i++) rig.Tick();
        Assert.NotNull(rig.Lesson.BaselineMs);
        Assert.Equal("Baseline", Assert.Single(rig.Lesson.History).Result);
        var portal = BrowserLesson.ReturnPortal;
        Assert.True(rig.Session.ServerController!.Pawn!.SetLocation(portal));
        Assert.True(rig.Session.LocalPawn!.SetLocation(portal));
        rig.Tick();
        Assert.Equal(1, rig.Lesson.Returns);
        Assert.InRange(rig.Session.LocalPawn.Location.Z, 640, 700);
        Assert.InRange(rig.Session.ServerController.Pawn.Location.Z, 640, 700);
        Assert.Null(rig.Lesson.RecoveryMs);
        Assert.Single(rig.Lesson.History);
        rig.Lesson.Action("drop");
        bool pressed = false;
        for (int i = 0; i < 600 && rig.Lesson.RecoveryMs is null; i++)
        {
            if (!pressed && rig.Session.LocalPawn.Location.Z < 150 && rig.Session.LocalController!.IsInState("PlayerLanding"))
            { rig.Lesson.Action("night"); pressed = true; }
            rig.Tick();
        }
        Assert.True(pressed);
        Assert.Equal(2, rig.Lesson.Attempts);
        Assert.Equal("Cancelled", rig.Lesson.History[^1].Result);
        Assert.True(rig.Lesson.History[^1].SavedMs > 0);
        Assert.NotNull(rig.Lesson.History[^1].SwitchMs);
        rig.Lesson.Action("reset");
        rig.Tick();
        Assert.Equal(2, rig.Lesson.Returns);
        Assert.Equal(2, rig.Lesson.History.Count);
    }
}
