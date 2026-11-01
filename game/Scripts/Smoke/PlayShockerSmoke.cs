using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-shocker</c>: the spy's Shocker Gun from the player's keys, as the launchers run the game (SteeE,
/// Play Level rules, the listen server on its own thread, one merc bot at normal, wall clock, HUD). Every press is a
/// Godot input event sent by <see cref="KeyPilot"/> for the key the spy layout binds (E fire mode, L laser, left mouse
/// fire), read by PlayInput and sent over loopback. The only test hook is on the server's thread: it holds the merc
/// bot in front of the spy until the first shock lands, holds its input during the later immunity observation,
/// and copies the bot's server state for the checks.
/// <para>
/// Steps (spec/equipment/spy-shocker.md, spy-shocker-owner-notes.md, sticky-shock-effect.md): hold E 0.8 s (the draw
/// starts after 0.5 s, no laser, nothing drawn), L (the laser on: beam and dot drawn), L (off), E (holster), tap E (the
/// draw starts at the release, laser on and drawn), aim at the bot with the mouse, fire until the server shocks it
/// (no health lost, the impact effect at the hit, the electric clip and the spark emitters on the client's bot), the
/// bot stays frozen, the shock ends (sparks removed), the amber immunity countdown persists until the authoritative
/// timer expires and clears, then three shots at the floor each leave an impact effect.
/// </para>
/// With a window and SVM_PLAY_SHOTS naming a folder (under local/) the window is saved after each step and at the key
/// moments inside steps.
/// </summary>
public sealed class PlayShockerSmoke : SmokeTest
{
    public const string TestName = "play-shocker";

    /// <summary>Drawn spy laser nodes join these groups (game/Scripts/Fx/SpyLaserView.cs).</summary>
    public const string GroupLaserBeam = "svm_spy_laser_beam";

    public const string GroupLaserDot = "svm_spy_laser_dot";

    /// <summary>Drawn sticky shock sparks join this group (game/Scripts/Fx/ShockFxView.cs).</summary>
    public const string GroupShockSparks = "svm_shock_sparks";

    private const int TeardownFrames = 6;
    private static readonly Name ClassShockEmitter = new("SEffectShockedPawnEmitter");

    private readonly List<string> _done = new();
    private readonly List<StepDef> _steps = new();
    private readonly ServerWatch _server = new();
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private int _index;
    private int _frame;
    private long _stepTick;
    private int _teardown = -1;
    private SmokeResult _final;
    private int _shots;
    private bool _hooked;
    private object? _memo;

    private sealed record StepDef(string Name, double TimeoutSeconds, Action<PlayShockerSmoke> Act, Func<PlayShockerSmoke, string?> Done);

    public override string Name => TestName;

    public PlayShockerSmoke()
    {
        _steps.Add(new StepDef("settle", 60, _ => { }, s => s.Walking && s.Ticks > 90 && s._keys!.Ready ? $"in {s.PC!.GetStateName()}" : null));
        // Night vision first, so the screenshots of a dark SteeE show what the shots do (the laser draws in every mode).
        _steps.Add(new StepDef("night vision (F1)", 4, s => s.TapAt("IK_F1", 2), s => s.PC is { RendMap: not 0 } pc && s.Ticks > 10 ? $"vision {pc.RendMap}" : null));
        _steps.Add(new StepDef("hold E 0.8 s: slow draw, no laser", 8, s => s.HoldDraw(), s => s.HoldDrawDone()));
        _steps.Add(new StepDef("L: laser on", 4, s => s.TapAt("SwitchLaser", 2), s => s.LaserDrawn(true)));
        _steps.Add(new StepDef("L: laser off", 4, s => s.TapAt("SwitchLaser", 2), s => s.LaserDrawn(false)));
        _steps.Add(new StepDef("tap E: holster", 6, s => s.TapAt("FireMode", 2), s => s.Pawn is { WeaponLoaded: 0 } && s.Unarmed && s.Ticks > 20 ? $"holstered, {s.PC!.GetStateName()}" : null));
        _steps.Add(new StepDef("tap E: quick draw with the laser", 6, s => s.TapDraw(), s => s.TapDrawDone()));
        _steps.Add(new StepDef("hold a merc bot in front (server setup)", 20, s =>
        {
            s._server.Pinning = true;
            s.UnstickClientBot();
        }, s => s.BotHeld()));
        _steps.Add(new StepDef("aim at the bot (mouse)", 10, s => s.AimAtBot(), s => s.Aimed()));
        _steps.Add(new StepDef("shock the bot (left mouse)", 25, s => s.ShootBot(), s => s.BotShocked()));
        _steps.Add(new StepDef("the shocked bot stays frozen", 6, s => s.AimAtBot(), s => s.BotFrozen()));
        _steps.Add(new StepDef("the shock ends", 12, _ => { }, s => s.ShockOver()));
        _steps.Add(new StepDef("shock immunity counts down and expires", 20, s =>
        {
            s._server.Pinning = true;
            s._server.HoldCombat = true;
            s.AimAtBot();
        }, s => s.ImmunityExpires()));
        _steps.Add(new StepDef("three shots at a wall: an impact each", 25, s => s.ShootWall(), s => s.WallImpacts()));

        // SVM_SHOCKER_SKIP: steps to leave out, by the start of their name (comma separated), to look at one area.
        string? skip = System.Environment.GetEnvironmentVariable("SVM_SHOCKER_SKIP");
        if (!string.IsNullOrWhiteSpace(skip))
        {
            string[] names = skip.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _steps.RemoveAll(st => st.Name != "settle" && Array.Exists(names, n => st.Name.StartsWith(n, StringComparison.OrdinalIgnoreCase)));
        }
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
            _root = PlayInspectSmoke.StartPlay(context, bots: 1);
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return Finish(SmokeResult.Fail("play scene did not load: " + error));
        }

        if (!_root.IsLoaded || _root.Session is not { } session || session.LocalPawn is null)
        {
            return SmokeResult.Running;
        }

        if (session.ServerFailure is { } failure)
        {
            return Finish(SmokeResult.Fail("server failed: " + failure.Message));
        }

        if (!_hooked)
        {
            if (session.Host is not { } host)
            {
                return Finish(SmokeResult.Fail("no listen server in this process"));
            }

            _server.Bots = session.Bots;
            host.BeforeWorldTick += _server.OnServerTick;
            _keys = KeyPilot.Attach(_root);
            _hooked = true;
            _stepTick = session.TicksRun;
        }

        _keys!.Update();
        StepDef step = _steps[_index];
        string? detail = _frame == 0 ? null : step.Done(this);
        if (_failed is not null)
        {
            Shot(step.Name + " FAILED");
            return Finish(SmokeResult.Fail($"step '{step.Name}': {Describe()} | done: {string.Join(" | ", _done)}"));
        }

        if (detail is not null)
        {
            string line = $"{step.Name}: {detail}";
            _done.Add(line);
            GD.Print($"SVM_SMOKE {TestName} ok {line} ({Ticks} ticks)");
            GD.Print($"SVM_SMOKE {TestName} where: {Where()}");
            Shot(step.Name);
            _keys.ReleaseAll();
            _index++;
            _frame = 0;
            _memo = null;
            _stepTick = session.TicksRun;
            return _index >= _steps.Count ? Finish(SmokeResult.Pass(string.Join(" | ", _done))) : SmokeResult.Running;
        }

        if (Ticks > step.TimeoutSeconds * Sim.Hz)
        {
            Shot(step.Name + " FAILED");
            return Finish(SmokeResult.Fail($"step '{step.Name}' had no result after {step.TimeoutSeconds} s: {Describe()} | done: {string.Join(" | ", _done)}"));
        }

        // One step action a simulation tick: the aim steers the view from the last tick's rotation, and several mouse
        // moves a tick (a few hundred frames a second) overshot it.
        if (Session.TicksRun == _lastActTick)
        {
            return SmokeResult.Running;
        }

        _lastActTick = Session.TicksRun;
        step.Act(this);
        _frame++;
        return SmokeResult.Running;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        _keys?.ReleaseAll();
        if (_root?.Session?.Host is { } host)
        {
            host.BeforeWorldTick -= _server.OnServerTick;
        }

        _root?.QueueFree();
        _root = null;
        return SmokeResult.Running;
    }

    // ---- what the steps see -------------------------------------------------------------------------------------

    private PlaySession Session => _root!.Session!;

    private int Ticks => (int)(Session.TicksRun - _stepTick);

    private long _lastActTick = -1;

    private float Now => Session.Client.World.Level.TimeSeconds;

    private SPlayerController? PC => Session.LocalController;

    private SBasePawn? Pawn => Session.LocalPawn;

    private SWeaponAttaque? Pistol => Pawn?.MyWeapon as SWeaponAttaque;

    private bool Walking => PC?.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) == true;

    private bool Unarmed => PC is { } pc && (pc.IsInState("s_PlayerWalkingSt0H") || pc.IsInState("s_PlayerWalkingCr0H"));

    private bool Aiming => PC is { } pc && (pc.IsInState("s_PlayerWalkingSt2H") || pc.IsInState("s_PlayerWalkingCr2H"));

    /// <summary>The client's copy of the held merc bot.</summary>
    private SBasePawn? Bot
    {
        get
        {
            // Actor ids differ between the server and the client: the only merc of this match is the bot.
            return _server.Snapshot().BotId == 0 ? null : FindClientBot();
        }
    }

    private SBasePawn? FindClientBot()
    {
        foreach (SPAWNDEFENSE p in Session.Client.World.Level.AllActors<SPAWNDEFENSE>())
        {
            if (!p.bDeleteMe)
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>Visible drawn nodes of a group (what the player sees).</summary>
    private int Drawn(string group)
    {
        int n = 0;
        foreach (Node node in _root!.GetTree().GetNodesInGroup(group))
        {
            if (node is Node3D n3 && n3.IsVisibleInTree())
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>The spark emitters the client world holds on <paramref name="pawn"/>.</summary>
    private int ClientSparks(Actor? pawn)
    {
        int n = 0;
        foreach (Actor a in Session.Client.World.Level.AllActors<Actor>())
        {
            if (!a.bDeleteMe && a.ClassName == ClassShockEmitter && (pawn is null || ReferenceEquals(a.Owner, pawn)))
            {
                n++;
            }
        }

        return n;
    }

    private int Marks => _root!.Fx?.Marks ?? 0;

    private int FxSparks => _root!.Fx?.Sparks ?? 0;

    private void TapAt(string action, int frame)
    {
        if (_frame == frame)
        {
            _keys!.Tap(action);
        }
    }

    // ---- the draw modes -----------------------------------------------------------------------------------------

    private sealed class DrawMemo
    {
        public float PressedAt = -1f;
        public float ReleasedAt = -1f;
        public float DownAt = -1f;
        public float DrawStartedAt = -1f;
        public float DrawnAt = -1f;
        public bool LaserSeen;
        public int LaserDrawnFrames;
    }

    private DrawMemo Memo => (DrawMemo)(_memo ??= new DrawMemo());

    private void WatchDraw()
    {
        DrawMemo m = Memo;
        // The controller's byte: down once the press reached the client tick.
        if (m.PressedAt >= 0f && PC?.bFireMode != 0 && m.DownAt < 0f)
        {
            m.DownAt = Now;
        }

        if (m.DrawStartedAt < 0f && m.PressedAt >= 0f && (PC?.IsInState("s_PlayerWalkingMario_T_0HTo2H") == true || Pawn?.WeaponLoaded != 0))
        {
            m.DrawStartedAt = Now;
        }

        if (m.DrawnAt < 0f && Aiming)
        {
            m.DrawnAt = Now;
        }

        if (Pistol is { bLaserOn: true })
        {
            m.LaserSeen = true;
        }

        if (Drawn(GroupLaserBeam) + Drawn(GroupLaserDot) > 0)
        {
            m.LaserDrawnFrames++;
        }
    }

    private void HoldDraw()
    {
        DrawMemo m = Memo;
        if (_frame == 2)
        {
            _keys!.Press("FireMode");
            m.PressedAt = Now;
        }

        if (m.PressedAt >= 0f && m.ReleasedAt < 0f && Now - m.PressedAt >= 0.8f)
        {
            _keys!.Release("FireMode");
            m.ReleasedAt = Now;
        }

        WatchDraw();
    }

    private string? HoldDrawDone()
    {
        DrawMemo m = Memo;
        if (m.ReleasedAt < 0f || m.DrawnAt < 0f || Now - m.DrawnAt < 0.5f)
        {
            return null;
        }

        float delay = m.DrawStartedAt - m.DownAt;
        if (delay < 0.5f || delay > 0.55f)
        {
            return Fail($"the hold drew {delay:F3} s after the key went down (the original: the first tick past 0.5 s)");
        }

        if (m.LaserSeen || m.LaserDrawnFrames > 0)
        {
            return Fail($"the hold draw turned the laser on (replicated {m.LaserSeen}, drawn frames {m.LaserDrawnFrames})");
        }

        return $"draw started {delay:F3} s after the key went down, aimed {m.DrawnAt - m.DrawStartedAt:F3} s later, no laser";
    }

    private void TapDraw()
    {
        DrawMemo m = Memo;
        if (_frame == 2)
        {
            _keys!.Tap("FireMode");
            m.PressedAt = Now;
        }

        if (m.DownAt >= 0f && m.ReleasedAt < 0f && PC?.bFireMode == 0)
        {
            m.ReleasedAt = Now;
        }

        WatchDraw();
    }

    private string? TapDrawDone()
    {
        DrawMemo m = Memo;
        if (m.DrawnAt < 0f || m.ReleasedAt < 0f || Pistol is not { bLaserOn: true } || Drawn(GroupLaserBeam) == 0 || Drawn(GroupLaserDot) == 0)
        {
            return null;
        }

        float delay = m.DrawStartedAt - m.ReleasedAt;
        if (delay > 0.05f || delay < 0f)
        {
            return Fail($"the tap drew {delay:F3} s after the release (the original starts the draw at the release)");
        }

        return $"key held {m.ReleasedAt - m.DownAt:F3} s; draw started {delay:F3} s after the release, aimed {m.DrawnAt - m.DrawStartedAt:F3} s later; laser on, beam and dot drawn";
    }

    private string? LaserDrawn(bool on)
    {
        if (Ticks < 15 || Pistol is not { } pistol)
        {
            return null;
        }

        int beams = Drawn(GroupLaserBeam);
        int dots = Drawn(GroupLaserDot);
        // The SLaserBeam must be drawn once: from the hand by the laser view, not again at the simulation's place.
        if (pistol.LaserBeam is { } beamActor && _root!.Fx?.Effects?.MeshOf(beamActor) is { } twice && twice.IsVisibleInTree())
        {
            return Fail($"the beam is drawn twice: the effects view also shows the SLaserBeam mesh at {beamActor.Location}");
        }

        if (on)
        {
            return pistol.bLaserOn && beams > 0 && dots > 0 ? $"bLaserOn, beam {beams}, dot {dots} drawn{LaserWhere()}" : null;
        }

        return !pistol.bLaserOn && beams == 0 && dots == 0 ? "bLaserOn off, nothing drawn" : null;
    }

    private string Where()
    {
        SWeaponAttaque? p = Pistol;
        return string.Create(CultureInfo.InvariantCulture,
            $"pawn {Pawn?.Location} view {Session.View.Location} {Session.View.Rotation} beam {p?.LaserBeam?.Location} {p?.LaserBeam?.Rotation} {p?.LaserBeam?.DrawScale3D} dot {p?.LaserProjector?.Location} {p?.LaserProjector?.DrawScale}");
    }

    private string LaserWhere()
    {
        Actor? dot = Pistol?.LaserProjector;
        return dot is null ? "" : string.Create(CultureInfo.InvariantCulture, $", dot at {dot.Location} scale {dot.DrawScale:F2}");
    }

    // ---- the bot ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Part of the setup's teleport: the client's copy of the bot follows the server's jump with one swept move with
    /// world collision off (net-proxy.md rule 13), which other actors still block (MoveActor's mask keeps pawns and
    /// others). From some of the places a bot walks, the straight line up to the spot in front of the spy crosses a
    /// gameplay object; the copy stops against it and every later snap starts touching it, so the copy never arrives.
    /// By the decoded rules the original would do the same; no bot in a match jumps across the map. When the copy has
    /// not moved for a second while the server's bot is held more than a snap away, the copy is put where the server's
    /// is (once, logged); from then on the server's samples keep it there.
    /// </summary>
    private void UnstickClientBot()
    {
        ServerSnap s = _server.Snapshot();
        if (_botUnstuck || !s.Held || FindClientBot() is not { } bot || FVector.Dist(s.BotLocation, s.Spot) > 60f ||
            FVector.Dist(bot.Location, s.BotLocation) <= SnapRadii * bot.CollisionRadius)
        {
            _botStill = 0;
            return;
        }

        _botStill = bot.Location == _botLast ? _botStill + 1 : 0;
        _botLast = bot.Location;
        if (_botStill < Sim.Hz)
        {
            return;
        }

        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"SVM_SMOKE {TestName} note: the client's bot stopped at {bot.Location} on the way to {s.BotLocation} (snap blocked); placed there"));
        bot.SetLocation(s.BotLocation);
        bot.ErrorDistance = default;
        bot.Velocity = default;
        _botUnstuck = true;
    }

    /// <summary>The proxy snap distance in collision radii (net-proxy.md rule 13).</summary>
    private const float SnapRadii = 6f;

    private bool _botUnstuck;
    private int _botStill;
    private FVector _botLast;

    private string? BotHeld()
    {
        ServerSnap s = _server.Snapshot();
        return s.Held && Bot is { } bot && FVector.Dist(bot.Location, s.Spot) < 60f && Ticks > 60
            ? $"bot at {bot.Location}, {FVector.Dist(bot.Location, Pawn!.Location):F0} uu from the spy, health {s.BotHealth}" : null;
    }

    private (int Yaw, int Pitch) AimError(FVector point)
    {
        if (PC is not { } pc || !Session.View.Valid)
        {
            return (int.MaxValue, int.MaxValue);
        }

        FRotator want = (point - Session.View.Location).Rotation();
        return ((short)(want.Yaw - pc.Rotation.Yaw), (short)(want.Pitch - pc.Rotation.Pitch));
    }

    private void Look((int Yaw, int Pitch) e)
    {
        if (Math.Abs(e.Yaw) <= 60 && Math.Abs(e.Pitch) <= 60)
        {
            return;
        }

        // MouseLook consumes raw counts at every resolution. Scaling by the tiny headless viewport would
        // overshoot the target each tick and prevent this input-driven pilot from ever settling.
        _keys!.Look(Math.Clamp(e.Yaw / KeyPilot.UnitsPerCount, -30f, 30f), Math.Clamp(-e.Pitch / KeyPilot.UnitsPerCount, -20f, 20f));
        Input.FlushBufferedEvents();
    }

    private FVector BotAimPoint(SBasePawn bot) => bot.Location + new FVector(0f, 0f, bot.CollisionHeight * 0.4f);

    private void AimAtBot()
    {
        if (Bot is { } bot)
        {
            Look(AimError(BotAimPoint(bot)));
        }
    }

    private string? Aimed()
    {
        if (Bot is not { } bot)
        {
            return null;
        }

        (int yaw, int pitch) = AimError(BotAimPoint(bot));
        return Math.Abs(yaw) < 150 && Math.Abs(pitch) < 150 && Ticks > 20 ? $"aim error yaw {yaw}, pitch {pitch}" : null;
    }

    private sealed class ShotMemo
    {
        public int Marks0;
        public int FxSparks0;
        public float Energy0;
        public int Muzzle0;
        public float ShockedAt = -1f;
        public List<string> BotClips = new();
        public int MaxClientSparks;
        public int MaxDrawnSparks;
        public int MaxSparkParticles;
        public int MaxSkinSurfaces;
        public int MaxImmunityShells;
        public bool SawTimedImmunity;
        public int Taps;
        public int? Yaw;
    }

    private ShotMemo Shots => (ShotMemo)(_memo ??= new ShotMemo
    {
        Marks0 = Marks,
        FxSparks0 = FxSparks,
        Energy0 = (Pawn as SPawnAttaque)?.EnergyLevel ?? 0f,
        Muzzle0 = Pistol?.MuzzleCount ?? 0,
    });

    private void WatchBot(ShotMemo m)
    {
        if (Bot is not { } bot)
        {
            return;
        }

        for (int c = 0; c < OpenScct.Game.Movement.PawnAnimation.ChannelCount; c++)
        {
            if (bot.PawnAnim[c].Sequence is { IsNone: false } seq)
            {
                string clip = c.ToString(CultureInfo.InvariantCulture) + ":" + seq.ToString();
                if (!m.BotClips.Contains(clip))
                {
                    m.BotClips.Add(clip);
                }
            }
        }

        m.MaxClientSparks = Math.Max(m.MaxClientSparks, ClientSparks(bot));
        m.MaxDrawnSparks = Math.Max(m.MaxDrawnSparks, Drawn(GroupShockSparks));
        // What the player sees: the class's own spark particles and the Stickess skin on the drawn bot.
        m.MaxSparkParticles = Math.Max(m.MaxSparkParticles, _root!.Fx?.Effects?.ParticlesOf(ClassShockEmitter.ToString()) ?? 0);
        m.MaxSkinSurfaces = Math.Max(m.MaxSkinSurfaces, _root.Pawns?.ViewOf(bot)?.StickySurfaces ?? 0);
        if (_root.Pawns?.ViewOf(bot) is { ShockImmunity: { Shown: true } cue } view)
        {
            ShockImmunityView immunity = MercShockPresentation.Read(bot, view.DrawAlpha);
            m.SawTimedImmunity |= immunity.Active && !immunity.StateProtected && immunity.RemainingSeconds > 0f;
            m.MaxImmunityShells = Math.Max(m.MaxImmunityShells, VisibleShells(cue));
        }
    }

    private void ShootBot()
    {
        ShotMemo m = Shots;
        AimAtBot();
        WatchBot(m);
        // A press every 0.75 s while the energy lasts (15 a shot); the server's first shock stops the presses.
        if (m.ShockedAt < 0f && _frame % 45 == 10 && (Pawn as SPawnAttaque)?.EnergyLevel >= 15f && Bot is { } bot &&
            AimError(BotAimPoint(bot)) is var e && Math.Abs(e.Yaw) < 200 && Math.Abs(e.Pitch) < 200)
        {
            _keys!.Tap("Fire");
            m.Taps++;
        }

        if (m.ShockedAt < 0f && _server.Snapshot().BotShocked)
        {
            m.ShockedAt = Now;
            _server.Pinning = false;
        }

        if (m.ShockedAt >= 0f && Now - m.ShockedAt is > 0.25f and < 0.3f)
        {
            Shot("shock the bot +0.25 s");
        }
    }

    private string? BotShocked()
    {
        ShotMemo m = Shots;
        if (m.ShockedAt < 0f || Now - m.ShockedAt < 1.0f || Bot is not { } bot)
        {
            return null;
        }

        ServerSnap s = _server.Snapshot();
        List<string> wrong = new();
        if (s.BotHealth != 100 || bot.Health != 100)
        {
            wrong.Add($"health {s.BotHealth} on the server, {bot.Health} on the client (the Shocker deals none)");
        }

        if (Marks <= m.Marks0)
        {
            wrong.Add("no impact effect drawn for the shots");
        }

        // The electric reaction plays on the electric channel (the mesh's own clip, PainStAlEc2 standing).
        string electric = SBasePawn.ELECTRICCHANNEL.ToString(CultureInfo.InvariantCulture) + ":";
        if (!m.BotClips.Exists(c => c.StartsWith(electric, StringComparison.Ordinal)))
        {
            wrong.Add($"the client's bot played no clip on the electric channel {SBasePawn.ELECTRICCHANNEL} (clips {string.Join(",", m.BotClips)})");
        }

        if (m.MaxClientSparks < 7)
        {
            wrong.Add($"the client's bot carried {m.MaxClientSparks} of the 7 SEffectShockedPawnEmitter sparks");
        }

        if (m.MaxDrawnSparks < 7)
        {
            wrong.Add($"{m.MaxDrawnSparks} of the 7 shock sparks drawn");
        }

        if (m.MaxSparkParticles == 0)
        {
            wrong.Add("no SEffectShockedPawnEmitter particle drawn (the class's sticky and aura sprites)");
        }

        if (m.MaxSkinSurfaces == 0)
        {
            wrong.Add("the shocked bot is drawn with its own skin (no Stickess shader)");
        }

        if (!s.Immunity.TimerBlocked || s.Immunity.StateProtected || !m.SawTimedImmunity || m.MaxImmunityShells == 0)
        {
            wrong.Add($"accepted shock has no timed amber protection: server {s.Immunity}, client timer {m.SawTimedImmunity}, drawn shells {m.MaxImmunityShells}");
        }

        // The laser keeps pointing where the spy looks (playtest: it turned back past the camera to the floor).
        if (Pistol?.LaserBeam is { } shotBeam && Session.View.Valid && (shotBeam.Rotation.Vector() | Session.View.Rotation.Vector()) < 0.5f)
        {
            wrong.Add($"the laser points away from the view after the shot: beam {shotBeam.Rotation}, view {Session.View.Rotation}");
        }

        if (wrong.Count > 0)
        {
            return Fail(string.Join("; ", wrong));
        }

        return $"shocked after {m.Taps} presses: server pad frozen {s.BotPad}, electric {s.BotElectric}, weapon {s.BotWeaponState}, health 100, " +
            $"impact effects {Marks - m.Marks0} (last at {_root!.Fx?.LastMarkAt}), sparks drawn {m.MaxDrawnSparks} ({m.MaxSparkParticles} particles), Stickess surfaces {m.MaxSkinSurfaces}, amber shells {m.MaxImmunityShells}, clips [{string.Join(",", m.BotClips)}]";
    }

    private string? BotFrozen()
    {
        ServerSnap s = _server.Snapshot();
        if (_memo is null)
        {
            _memo = s.BotLocation;
            return null;
        }

        var start = (FVector)_memo;
        if (s.BotPad == 0 && s.BotElectric)
        {
            // The freeze has ended (int(t + 3.5)), the electric time goes on.
            float moved = FVector.Dist(start, s.BotLocation);
            return moved < 16f ? $"frozen until the freeze ended (moved {moved:F1} uu), electric state goes on" : Fail($"the frozen bot moved {moved:F1} uu");
        }

        if (!s.BotElectric && s.BotPad == 0)
        {
            return Fail("the shock ended before the freeze was seen");
        }

        return null;
    }

    private string? ShockOver()
    {
        ServerSnap s = _server.Snapshot();
        if (s.BotElectric || Bot is not { } bot)
        {
            return null;
        }

        int sparks = ClientSparks(bot);
        int drawn = Drawn(GroupShockSparks);
        if (sparks != 0 || drawn != 0 || Ticks <= 10)
        {
            return null;
        }

        // A dry merc: freeze 3.5 s and electric 4.5 s, each ending on the first tick past the whole second
        // (sticky-shock-effect.md rules 3.13, 5 and 6).
        float t0 = s.TimeShock;
        float freezeDue = MathF.Truncate(t0 + 3.5f);
        float electricDue = MathF.Truncate(t0 + 4.5f);
        float tick = 1f / Sim.Hz;
        if (s.FreezeEndedAt <= freezeDue || s.FreezeEndedAt > freezeDue + 2f * tick ||
            s.ElectricEndedAt <= electricDue || s.ElectricEndedAt > electricDue + 2f * tick)
        {
            return Fail($"shock at {t0:F3} s: freeze ended {s.FreezeEndedAt:F3} (due past {freezeDue}), electric ended {s.ElectricEndedAt:F3} (due past {electricDue})");
        }

        if (_root!.Pawns?.ViewOf(bot) is not { ShockImmunity: { } cue } view)
        {
            return Fail("the merc has no drawn immunity cue after the electric effect ended");
        }

        ShockImmunityView immunity = MercShockPresentation.Read(bot, view.DrawAlpha);
        if (!s.Immunity.TimerBlocked || s.Immunity.StateProtected || !immunity.Active || immunity.StateProtected ||
            immunity.RemainingSeconds <= 0f || !cue.Shown || VisibleShells(cue) == 0)
        {
            return Fail($"amber immunity must outlast the electric effect: server {s.Immunity}, client {immunity}, shown {cue.Shown}");
        }

        return $"shock at {t0:F3} s: freeze ended {s.FreezeEndedAt:F3} s, electric {s.ElectricEndedAt:F3} s (int(t + 3.5), int(t + 4.5)); sparks removed, amber remains with {immunity.RemainingSeconds:F2} s left";
    }

    private static int VisibleShells(MercShockImmunityView cue)
    {
        int count = 0;
        foreach (MeshInstance3D shell in cue.Shells)
        {
            if (shell.IsVisibleInTree()) count++;
        }

        return count;
    }

    private sealed class ImmunityMemo
    {
        public float TimeShock;
        public float Remaining0;
        public float Coverage0;
        public bool CountdownSeen;
        public bool CoverageDrainSeen;
        public bool CountdownShot;
        public long? ClearTick;
    }

    private string? ImmunityExpires()
    {
        if (Bot is not { } bot || _root!.Pawns?.ViewOf(bot) is not { ShockImmunity: { } cue } view)
            return Fail("lost the merc or its amber cue while waiting for immunity expiry");

        ServerSnap s = _server.Snapshot();
        ShockImmunityView immunity = MercShockPresentation.Read(bot, view.DrawAlpha);
        if (_memo is null)
        {
            if (!s.Immunity.TimerBlocked || !immunity.Active || immunity.StateProtected || !cue.Shown)
                return Fail($"immunity countdown was not active at its first observation: server {s.Immunity}, client {immunity}");
            _memo = new ImmunityMemo { TimeShock = s.TimeShock, Remaining0 = immunity.RemainingSeconds, Coverage0 = cue.Coverage };
        }

        var m = (ImmunityMemo)_memo;
        if (s.TimeShock != m.TimeShock || s.Immunity.StateProtected || immunity.StateProtected)
            return Fail($"immunity changed its source during the countdown: shock {m.TimeShock:F3} -> {s.TimeShock:F3}, server {s.Immunity}, client {immunity}");

        m.CountdownSeen |= immunity.Active && m.Remaining0 - immunity.RemainingSeconds >= 1f;
        m.CoverageDrainSeen |= cue.Shown && m.Coverage0 - cue.Coverage >= 0.05f;
        if (!m.CountdownShot && cue.Shown && m.Remaining0 - immunity.RemainingSeconds >= 2f)
        {
            Shot("shock immunity countdown amber");
            m.CountdownShot = true;
        }
        if (s.Immunity.TimerBlocked)
        {
            if (!immunity.Active || !cue.Shown || VisibleShells(cue) == 0)
                return Fail($"amber cleared before authoritative immunity: server {s.Immunity}, client {immunity}, shown {cue.Shown}");
            float expectedCoverage = Math.Clamp(immunity.Fraction, MercShockImmunityView.MinimumCoverage, 1f);
            if (Math.Abs(cue.Coverage - expectedCoverage) > 0.02f)
                return Fail($"amber coverage {cue.Coverage:F3} does not follow the received countdown fraction {immunity.Fraction:F3}");
            return null;
        }

        // The source query, not an assumed duration or a sleep, decides expiry. Allow its final snapshot and draw
        // to arrive, then require the cleared presentation to remain clear for several simulation ticks.
        if (immunity.Active || cue.Shown || VisibleShells(cue) != 0)
        {
            m.ClearTick = null;
            return s.Time - s.ImmunityEndedAt > 1f ? Fail($"amber did not clear after authoritative expiry at {s.ImmunityEndedAt:F3}: client {immunity}, shown {cue.Shown}") : null;
        }

        m.ClearTick ??= Session.TicksRun;
        if (Session.TicksRun - m.ClearTick.Value < 6) return null;
        if (!m.CountdownSeen || !m.CoverageDrainSeen || s.ImmunityEndedAt < 0f)
            return Fail($"expiry did not exercise a visible countdown: seconds decreased {m.CountdownSeen}, body drained {m.CoverageDrainSeen}, source expiry {s.ImmunityEndedAt:F3}");

        _server.HoldCombat = false;
        return $"received countdown and body coverage drained from {m.Remaining0:F2} s / {m.Coverage0:P0}; authoritative expiry {s.ImmunityEndedAt:F3} s, status and amber shells cleared";
    }

    // ---- impacts ------------------------------------------------------------------------------------------------

    /// <summary>A third of the way down, a quarter turn right of where the step starts: the floor a few steps ahead, away from the bot.</summary>
    private const int WallPitch = -6000;

    private void ShootWall()
    {
        ShotMemo m = Shots;
        if (PC is not { } pc || Pawn is not SPawnAttaque spy)
        {
            return;
        }

        // A shot leaves a mark only on world geometry: TraceFireWithPos hands any other actor (a pawn, an
        // SBasicGamePlayObject crate) TakeDamage and adds no impact, as the original script does. So the shots go at
        // the first yaw from a quarter turn right whose line meets world geometry, and the released bot is held back on
        // its spot in front of the spy, out of that line.
        _server.Pinning = true;
        m.Yaw ??= WallYaw(pc.Rotation.Yaw + 16384);
        (int Yaw, int Pitch) error = ((short)(m.Yaw.Value - pc.Rotation.Yaw), (short)(WallPitch - pc.Rotation.Pitch));
        Look(error);
        // A press each 0.75 s once the view has turned and the energy allows it; each shot must add one impact effect.
        if (m.Taps < 3 && _frame % 45 == 30 && spy.EnergyLevel >= 15f && Math.Abs(error.Yaw) < 600 && Math.Abs(error.Pitch) < 600)
        {
            if (m.Taps == 0)
            {
                m.Marks0 = Marks;
                m.FxSparks0 = FxSparks;
                m.Muzzle0 = Pistol?.MuzzleCount ?? 0;
            }

            _keys!.Tap("Fire");
            m.Taps++;
        }
    }

    private string? WallImpacts()
    {
        ShotMemo m = Shots;
        if (m.Taps < 3 || Pistol is not { } pistol || pistol.MuzzleCount - m.Muzzle0 < 3 || Ticks < 30)
        {
            return null;
        }

        int marks = Marks - m.Marks0;
        int sparks = FxSparks - m.FxSparks0;
        return marks >= 3 && sparks >= 3 ? $"3 shots: {marks} impact marks, {sparks} spark bursts, last at {_root!.Fx?.LastMarkAt}"
            : Fail($"3 shots left {marks} impact marks and {sparks} spark bursts; the line from the eyes hits {LineHit()}");
    }

    /// <summary>The yaw nearest <paramref name="from"/> (steps of 2048 either way) whose line at the wall pitch meets world geometry.</summary>
    private int WallYaw(int from)
    {
        if (Pawn is not { } pawn)
        {
            return from;
        }

        // From the spy's eyes, where the shots start (the camera stands behind him in third person).
        FVector start = pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);
        for (int k = 0; k < 32; k++)
        {
            int yaw = from + ((k + 1) / 2 * ((k & 1) == 0 ? 1 : -1) * 2048);
            FVector end = start + (new FRotator(WallPitch, yaw, 0).Vector() * 3000f);
            if (OpenScct.Game.Equipment.GadgetWorld.Line(pawn, end, start, ETraceFlags.TRACE_AllColliding, out FCheckResult hit) &&
                (hit.Actor is null || hit.Actor.bWorldGeometry || hit.Actor is LevelInfo))
            {
                return yaw;
            }
        }

        return from;
    }

    /// <summary>What the line from the spy's eyes along the view meets in the client world (about the shots' first hit).</summary>
    private string LineHit()
    {
        if (Pawn is not { } pawn)
        {
            return "-";
        }

        FVector start = pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);
        FVector end = start + (Session.View.Rotation.Vector() * 15000f);
        return OpenScct.Game.Equipment.GadgetWorld.Line(pawn, end, start, ETraceFlags.TRACE_AllColliding, out FCheckResult hit)
            ? string.Create(CultureInfo.InvariantCulture, $"{hit.Actor?.GetUcClass().Name ?? "world"} at {FVector.Dist(start, hit.Location):F0} uu ({hit.Location})")
            : "nothing";
    }

    // ---- results ------------------------------------------------------------------------------------------------

    private string? Fail(string why)
    {
        _failed = why;
        return null;
    }

    private string? _failed;

    private string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"{(_failed is null ? "" : "FAILED: " + _failed + "; ")}state={PC?.GetStateName()} rot={PC?.Rotation} wl={Pawn?.WeaponLoaded} laser={Pistol?.bLaserOn} beams={Drawn(GroupLaserBeam)} dots={Drawn(GroupLaserDot)} " +
        $"energy={(Pawn as SPawnAttaque)?.EnergyLevel:F0} muzzle={Pistol?.MuzzleCount} taps={(_memo as ShotMemo)?.Taps} canFire={PC?.CheckIfCanFire()} marks={Marks} fxSparks={FxSparks} bot={Bot?.Location} botSparks={ClientSparks(Bot)} drawnSparks={Drawn(GroupShockSparks)} " +
        $"server={_server.Snapshot()} missing=[{string.Join(",", _keys?.Missing ?? [])}]");

    private void Shot(string step)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(folder);
        Image image = _root.GetViewport().GetTexture().GetImage();
        string safe = string.Concat(step.Split(System.IO.Path.GetInvalidFileNameChars())).Replace(' ', '_');
        image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{_shots++:D2}-{safe}.png")));
    }

    // ---- the server's thread ------------------------------------------------------------------------------------

    private readonly record struct ServerSnap(bool Held, FVector Spot, int BotId, int BotHealth, bool BotShocked, int BotPad,
        bool BotElectric, string BotWeaponState, FVector BotLocation, float Time, float TimeShock, float FreezeEndedAt,
        float ElectricEndedAt, ShockImmunityState Immunity, float ImmunityEndedAt);

    /// <summary>Runs on the listen server's thread before each world step; the main thread reads its snapshot.</summary>
    private sealed class ServerWatch
    {
        private readonly object _gate = new();
        private ServerSnap _snap;
        private FVector? _spot;
        private float _freezeEnded = -1f;
        private float _electricEnded = -1f;
        private float _immunityEnded = -1f;
        private bool _wasFrozen;
        private bool _wasElectric;

        public volatile bool Pinning;
        public volatile bool HoldCombat;
        public OpenScct.Bots.ServerBots? Bots;

        public ServerSnap Snapshot()
        {
            lock (_gate)
            {
                return _snap;
            }
        }

        public void OnServerTick(OpenScct.Server.ServerHost host)
        {
            Level level = host.World.Level;
            SPawnAttaque? spy = null;
            SPAWNDEFENSE? bot = null;
            foreach (SBasePawn p in level.AllActors<SBasePawn>())
            {
                if (p.bDeleteMe || p.Health <= 0)
                {
                    continue;
                }

                if (p is SPawnAttaque a)
                {
                    spy ??= a;
                }
                else if (p is SPAWNDEFENSE d)
                {
                    bot ??= d;
                }
            }

            if (spy is null || bot is null)
            {
                return;
            }

            if (Pinning)
            {
                Pin(spy, bot);
            }

            var pc = bot.Controller as SPlayerController;
            if (pc is not null && Bots?.Of(pc) is { } brain)
            {
                // The added countdown observation outlasts the stun. Keep its target in view without allowing
                // its AI to kill the observing spy; the actual pawn/weapon timers and damage rules keep ticking.
                brain.Held = HoldCombat;
                if (HoldCombat) OpenScct.Game.AI.AiInput.Idle.ApplyTo(pc);
            }
            int pad = pc?.iDontUpdatePadInput ?? 0;
            if (_wasFrozen && pad == 0 && _freezeEnded < 0f)
            {
                _freezeEnded = level.TimeSeconds;
            }

            if (_wasElectric && !bot.bStickyShocking && _electricEnded < 0f)
            {
                _electricEnded = level.TimeSeconds;
            }

            _wasFrozen |= pad != 0;
            _wasElectric |= bot.bStickyShocking;
            ShockImmunityState immunity = MercShockImmunity.Capture(bot);
            if (_wasElectric && !immunity.Active && _immunityEnded < 0f)
            {
                _immunityEnded = level.TimeSeconds;
            }

            ServerSnap snap = new(_spot is not null && Pinning, _spot ?? default, bot.ActorId, bot.Health,
                pc is not null && pc.TimeShock > 0f && bot.bStickyShocking, pad, bot.bStickyShocking,
                bot.MyWeapon?.GetStateName().ToString() ?? "-", bot.Location, level.TimeSeconds, pc?.TimeShock ?? 0f,
                _freezeEnded, _electricEnded, immunity, _immunityEnded);
            lock (_gate)
            {
                _snap = snap;
            }
        }

        private void Pin(SPawnAttaque spy, SPAWNDEFENSE bot)
        {
            if (_spot is null)
            {
                FVector eye = spy.Location + new FVector(0f, 0f, spy.EyeHeight);
                int yaw = spy.Controller?.Rotation.Yaw ?? spy.Rotation.Yaw;
                for (float d = 450f; d >= 150f && _spot is null; d -= 50f)
                {
                    for (int k = 0; k < 32 && _spot is null; k++)
                    {
                        int step = (k + 1) / 2 * ((k & 1) == 0 ? 1 : -1);
                        FVector dir = new FRotator(0, yaw + (step * 2048), 0).Vector();
                        FVector far = spy.Location + (dir * (d + 60f));
                        if (!GadgetWorld.Line(spy, far + new FVector(0f, 0f, spy.EyeHeight), eye, ETraceFlags.TRACE_World, out _) &&
                            !GadgetWorld.Line(spy, far, spy.Location, ETraceFlags.TRACE_World, out _))
                        {
                            _spot = spy.Location + (dir * d);
                        }
                    }
                }

                if (_spot is null)
                {
                    return;
                }
            }

            FVector spot = _spot.Value;
            float floor = spy.Location.Z - spy.CollisionHeight;
            if (GadgetWorld.Line(spy, new FVector(spot.X, spot.Y, floor - 300f), new FVector(spot.X, spot.Y, floor + 100f), ETraceFlags.TRACE_World, out FCheckResult ground))
            {
                floor = ground.Location.Z;
            }

            spot.Z = floor + bot.CollisionHeight + 1f;
            _spot = spot;
            float dx = bot.Location.X - spot.X;
            float dy = bot.Location.Y - spot.Y;
            if ((dx * dx) + (dy * dy) > 30f * 30f)
            {
                bot.SetLocation(spot);
            }

            bot.Velocity = new FVector(0f, 0f, bot.Velocity.Z);
            bot.Acceleration = default;
            bot.Rotation = new FRotator(0, (spy.Location - spot).Rotation().Yaw, 0);
        }
    }
}
