using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-inspect</c>: SteeE as the spy with three bots, set up as the launcher does (Play Level rules,
/// host thread, wall clock, HUD). Once the spy is drawn, <see cref="KeyPilot"/> plays about 40 s at the keys (walk,
/// look around, run, back and sideways, crouch and crouched walk, stand, jumps, Escape) with every
/// <see cref="PlayInspector"/> check on. Passes only when nothing was found; otherwise every issue is printed on its own
/// <c>SVM_INSPECT</c> line and the test fails with the list. See docs/TESTING-PLAY.md.
/// </summary>
public sealed class PlayInspectSmoke : SmokeTest
{
    public const string TestName = "play-inspect";

    public const int BotCount = 3;

    /// <summary>Seconds to wait for the spy to be drawn in a walking state.</summary>
    public const double SpawnTimeoutSeconds = 90.0;

    /// <summary>Seconds after the timeline before the end-of-run checks (the pause menu's wait).</summary>
    public const double SettleSeconds = 2.0;

    /// <summary>A walking step that moves the pawn less than this (uu) means the keys do not reach it.</summary>
    public const float MinWalk = 100f;

    private const int TeardownFrames = 6;

    private PlayRoot? _root;
    private KeyPilot? _keys;
    private PlayInspector? _inspector;
    private SmokeResult _final;
    private int _teardown = -1;
    private double _doneAt = -1;
    private double _nextShot;
    private double _lastIssueShot = -10;
    private string _lastStep = "";
    private FVector _stepStart;
    private bool _stepCrouchedAtStart;
    private int _stepYaw;
    private bool _airborne;
    private readonly List<string> _stepLog = [];

    public override string Name => TestName;

    /// <summary>
    /// A play scene on SteeE as the spy, the way <c>--svm-play=SteeE --side=spy --rules=playlevel --bots=N
    /// --difficulty=normal</c> starts one (host thread, wall clock, HUD), added to the tree.
    /// </summary>
    public static PlayRoot StartPlay(SmokeContext context, int bots = BotCount, string difficulty = "normal",
        OpenScct.Net.SimLinkProfile? link = null, OpenScct.Net.NetProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
        PlayConfig config = launcher with
        {
            Settings = launcher.Settings with
            {
                Map = "SteeE",
                Side = PlaySide.Spy,
                Mode = "Story",
                Rules = PlayRules.PlayLevel,
                HostMode = link is null ? PlayHostMode.Thread : PlayHostMode.Inline,
                SimLink = link,
                NetworkProfile = profile ?? launcher.Settings.NetworkProfile,
                Bots = bots,
                Difficulty = difficulty,
            },
            Render = true,
            Hud = true,
            TicksPerFrame = 0,
            Pilot = null,
        };
        PlayRoot root = PlayRoot.Create(config);
        context.Tree.Root.AddChild(root);
        return root;
    }

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            return ++_teardown < TeardownFrames ? SmokeResult.Running : _final;
        }

        if (_root is null)
        {
            _root = StartPlay(context);
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return Finish(SmokeResult.Fail("play scene did not load: " + error));
        }

        if (!_root.IsLoaded || _root.Session is not { } session)
        {
            return SmokeResult.Running;
        }

        if (session.ServerFailure is { } failure)
        {
            return Finish(SmokeResult.Fail("server failed: " + failure.Message));
        }

        if (session.LoginError is { } login)
        {
            return Finish(SmokeResult.Fail("login refused: " + login));
        }

        if (_inspector is null)
        {
            _inspector = new PlayInspector(_root);
            _inspector.Attach();
            _inspector.IssueAdded += issue =>
            {
                GD.Print("SVM_INSPECT new " + issue);
                if (_inspector.Seconds - _lastIssueShot > 1.0)
                {
                    _lastIssueShot = _inspector.Seconds;
                    SaveShot("issue");
                }
            };
        }

        _inspector.Frame();
        if (_keys is null)
        {
            return WaitForSpawn(session, context);
        }

        _keys.Update();
        _inspector.Phase = _keys.Step;
        _airborne |= session.LocalPawn is { Physics: OpenScct.Core.Engine.EPhysics.PHYS_Falling };
        OnStepChange(session);
        if (_inspector.Seconds >= _nextShot)
        {
            _nextShot = _inspector.Seconds + 2.0;
            SaveShot(_keys.Step.Length == 0 ? "setup" : _keys.Step.Replace(' ', '-'));
        }

        if (!_keys.Done)
        {
            return SmokeResult.Running;
        }

        if (_doneAt < 0)
        {
            _doneAt = _inspector.Seconds;
        }

        if (_inspector.Seconds - _doneAt < SettleSeconds)
        {
            return SmokeResult.Running;
        }

        return Finish(Verdict());
    }

    private SmokeResult WaitForSpawn(PlaySession session, SmokeContext context)
    {
        SPlayerController? pc = session.LocalController;
        SBasePawn? pawn = session.LocalPawn;
        bool drawn = pawn is not null && _root!.Pawns?.ViewOf(pawn) is not null;
        bool walking = pc is not null && (pc.IsInState("s_PlayerWalkingCr0H") || pc.IsInState("s_PlayerWalkingSt0H") || pc.IsInState("s_PlayerWalkingSt2H"));
        if (drawn && walking)
        {
            _keys = KeyPilot.Attach(_root!);
            _keys.Play(Timeline(pawn!));
            _stepStart = pawn!.Location;
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"SVM_PLAY {TestName} spy drawn at {pawn.Location} in {pc!.GetStateName()} after {context.ElapsedSeconds:F1} s (crouched {pawn.bIsCrouched})"));
            return SmokeResult.Running;
        }

        if (context.ElapsedSeconds > SpawnTimeoutSeconds)
        {
            return Finish(SmokeResult.Fail(string.Create(CultureInfo.InvariantCulture,
                $"the spy was not drawn in a walking state after {SpawnTimeoutSeconds:F0} s (controller {pc?.GetStateName()}, pawn {pawn?.Location}, drawn {drawn})")));
        }

        return SmokeResult.Running;
    }

    /// <summary>About 40 s at the keys. The spy may spawn crouched: then the first step stands up.</summary>
    private static List<KeyStep> Timeline(SBasePawn pawn)
    {
        var steps = new List<KeyStep>();
        if (pawn.bIsCrouched)
        {
            steps.Add(new KeyStep("stand up", 1.0) { Tap = ["bCrouch"] });
        }

        // The walks go back and forth over the same line (looks are undone) so they stay in the space already walked.
        steps.AddRange(
        [
            new KeyStep("walk", 4.0) { Hold = ["BT_Up"] },
            new KeyStep("look around", 2.5) { LookPerSecond = new Vector2(400f, 0f) },
            new KeyStep("look back", 2.5) { LookPerSecond = new Vector2(-400f, 0f) },
            new KeyStep("walk back", 3.0) { Hold = ["BT_Down"] },
            new KeyStep("fast speed", 0.4) { Tap = ["ChangeSpeed"] },
            new KeyStep("run", 3.0) { Hold = ["BT_Up"] },
            new KeyStep("run back", 2.5) { Hold = ["BT_Down"] },
            new KeyStep("strafe left", 1.5) { Hold = ["BT_Left"] },
            new KeyStep("strafe right", 1.5) { Hold = ["BT_Right"] },
            new KeyStep("slow speed", 0.4) { Tap = ["ChangeSpeed"] },
            new KeyStep("walk turning", 2.5) { Hold = ["BT_Up"], LookPerSecond = new Vector2(250f, 0f) },
            new KeyStep("crouch", 1.2) { Tap = ["bCrouch"] },
            new KeyStep("crouch walk", 4.0) { Hold = ["BT_Up"], LookPerSecond = new Vector2(150f, 0f) },
            new KeyStep("crouch still", 1.5),
            new KeyStep("stand", 1.6) { Tap = ["bCrouch"] },
            new KeyStep("jump", 1.8) { Tap = ["Jump"] },
            new KeyStep("jump forward", 2.2) { Hold = ["BT_Up"], Tap = ["Jump"] },
            new KeyStep("look up", 1.0) { LookPerSecond = new Vector2(0f, -250f) },
            new KeyStep("look down", 1.0) { LookPerSecond = new Vector2(0f, 250f) },
            new KeyStep("menu", 2.0) { Tap = ["ShowMenu"] },
        ]);
        return steps;
    }

    /// <summary>At each step change: checks what the step before did to the pawn, then notes the new step's start.</summary>
    private void OnStepChange(PlaySession session)
    {
        string step = _keys!.Step;
        if (step == _lastStep)
        {
            return;
        }

        SBasePawn? pawn = session.LocalPawn;
        if (pawn is not null && _lastStep.Length > 0)
        {
            FVector moved = pawn.Location - _stepStart;
            float horizontal = MathF.Sqrt((moved.X * moved.X) + (moved.Y * moved.Y));
            _stepLog.Add(string.Create(CultureInfo.InvariantCulture,
                $"{_lastStep}: moved {horizontal:F0} uu, yaw {_stepYaw}->{session.LocalController?.Rotation.Yaw}, crouched {_stepCrouchedAtStart}->{pawn.bIsCrouched}, fast {session.LocalController?.bFastSpeed}, airborne {_airborne}, physics {pawn.Physics}{Blocked(pawn, session)}"));
            CheckStep(_lastStep, horizontal, pawn, session);
        }

        if (step == "menu")
        {
            _inspector!.ExpectPauseMenu();
        }

        _lastStep = step;
        if (pawn is not null)
        {
            _stepStart = pawn.Location;
            _stepCrouchedAtStart = pawn.bIsCrouched;
            _stepYaw = session.LocalController?.Rotation.Yaw ?? 0;
            _airborne = false;
        }
    }

    private void CheckStep(string step, float moved, SBasePawn pawn, PlaySession session)
    {
        PlayInspector inspector = _inspector!;
        switch (step)
        {
            case "walk" or "run" or "crouch walk" or "walk back" or "run back" when moved < MinWalk && Blocked(pawn, session).Length == 0:
                inspector.Note(InspectChecks.Input, "no move " + step, string.Create(CultureInfo.InvariantCulture,
                    $"holding {_keys!.KeyOf(step.EndsWith("back", StringComparison.Ordinal) ? "BT_Down" : "BT_Up")} ({step}) moved the spy {moved:F0} uu (less than {MinWalk:F0}) with no wall in the way"));
                break;
            case "stand up" or "stand" when pawn.bIsCrouched:
                inspector.Note(InspectChecks.Input, "no stand " + step, $"tapping {_keys!.KeyOf("bCrouch")} did not stand the spy up ({step})");
                break;
            case "jump" or "jump forward" when !_airborne:
                inspector.Note(InspectChecks.Input, "no jump " + step, $"tapping {_keys!.KeyOf("Jump")} ({step}) never made the spy fall (PHYS_Falling)");
                break;
            case "crouch" when !pawn.bIsCrouched:
                inspector.Note(InspectChecks.Input, "no crouch", $"tapping {_keys!.KeyOf("bCrouch")} did not crouch the spy");
                break;
            case "fast speed" when session.LocalController is { bFastSpeed: false }:
                inspector.Note(InspectChecks.Input, "no fast speed", $"tapping {_keys!.KeyOf("ChangeSpeed")} (ChangeSpeed) did not set fast speed");
                break;
        }
    }

    /// <summary>" (wall N uu ahead)" or behind when world geometry is close in front of or behind the pawn, else "".</summary>
    private static string Blocked(SBasePawn pawn, PlaySession session)
    {
        if (session.Client.Map?.Physics?.Collision is not { } collision || session.LocalController is not { } pc)
        {
            return "";
        }

        new FRotator(0, pc.Rotation.Yaw, 0).GetAxes(out FVector forward, out _, out _);
        var extent = new FVector(pawn.CollisionRadius * 0.5f, pawn.CollisionRadius * 0.5f, pawn.CollisionHeight * 0.5f);
        foreach ((string where, float sign) in new[] { ("ahead", 1f), ("behind", -1f) })
        {
            FVector end = pawn.Location + (forward * (sign * (pawn.CollisionRadius + 40f)));
            if (collision.LineCheck(out OpenScct.Physics.Collision.FCheckResult hit, pawn, end, pawn.Location, extent, PlayInspector.WorldTrace))
            {
                return string.Create(CultureInfo.InvariantCulture, $" (wall {(hit.Location - pawn.Location).Size():F0} uu {where})");
            }
        }

        return "";
    }

    private SmokeResult Verdict()
    {
        PlayInspector inspector = _inspector!;
        foreach (string missing in _keys!.Missing)
        {
            inspector.Note(InspectChecks.Input, "unbound " + missing, $"no key is bound to '{missing}' in {_keys.Bindings.Source}");
        }

        if (_keys.Delivered == 0)
        {
            inspector.Note(InspectChecks.Input, "not delivered", $"none of the {_keys.Sent} input events reached the scene");
        }

        string report = inspector.Report();
        GD.Print($"SVM_INSPECT keys: {string.Join("; ", _keys.Notes)}; sent {_keys.Sent} delivered {_keys.Delivered}");
        foreach (string line in _stepLog)
        {
            GD.Print("SVM_INSPECT step " + line);
        }

        foreach (InspectIssue issue in inspector.Issues)
        {
            GD.Print("SVM_INSPECT issue " + issue);
        }

        GD.Print("SVM_INSPECT summary " + inspector.Summary());
        return inspector.Issues.Count == 0
            ? SmokeResult.Pass("no issues: " + inspector.Summary())
            : SmokeResult.Fail(report);
    }

    /// <summary>With a window and SVM_PLAY_SHOTS naming a folder (keep it under local/), saves the window.</summary>
    private void SaveShot(string label)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null || _inspector is null)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(folder);
        Image image = _root.GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{_inspector.Seconds:000.0}-{label}.png")));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        _keys?.ReleaseAll();
        _root?.QueueFree();
        return SmokeResult.Running;
    }
}
