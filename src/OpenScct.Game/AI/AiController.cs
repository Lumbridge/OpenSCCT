using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

/// <summary>What a brain wants this tick: the view to turn to, the keys, the stance.</summary>
public struct AiPlan
{
    /// <summary>The view yaw and pitch (65536 a turn) to turn to, when set.</summary>
    public int? Yaw;

    public int? Pitch;

    /// <summary>Largest view error (65536 a turn) at which the movement keys still apply; beyond it the bot turns first.</summary>
    public int Tolerance;

    /// <summary>
    /// The direction to move in (yaw), when the view looks elsewhere (<see cref="Yaw"/>): the forward key becomes the
    /// camera-relative keys for that direction, as a player strafes while watching a point.
    /// </summary>
    public int? MoveYaw;

    public AiInput Keys;

    /// <summary>The stance wanted (true crouched), or null to keep it.</summary>
    public bool? Crouched;
}

/// <summary>
/// One server-side bot: a player controller logged in like any player, whose input bytes, view and key presses are
/// written by this class before each world tick. It perceives through <see cref="AiSight"/> and
/// <see cref="AiNoiseBoard"/> into an <see cref="AiMemory"/>, lets its side's brain choose and act, and turns the view
/// no faster than its difficulty's turn rate. Godot-free and deterministic: its random stream is seeded from the world's
/// match seed and the bot's index.
/// </summary>
public sealed class AiController
{
    private readonly List<AiMessage> _messages = new();
    private readonly List<Pawn> _enemies = new();
    private float _nextSight;
    private int _crouchFrames;
    private float _crouchAt = -10f;
    private float _lastTick = -1f;
    private int _lastHealth;
    private SBasePawn? _pawn;

    public AiController(AiWorld world, SPlayerController controller, AiDifficulty difficulty, int index)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Difficulty = difficulty ?? throw new ArgumentNullException(nameof(difficulty));
        Index = index;
        Random = world.Level.World.CreateRandom(1000 + index, 77);
        TeamPlay = new TeamAi(this);
        Evasion = new SpyEvasion(this);
        Gadgets = new SpyGadgets(this);
        _nextSight = index * 0.017f;
    }

    public AiWorld World { get; }

    public SPlayerController Controller { get; }

    public AiDifficulty Difficulty { get; set; }

    public int Index { get; }

    public SimRandom Random { get; }

    public AiMemory Memory { get; } = new();

    /// <summary>The brain of the current pawn's side (made when the pawn arrives).</summary>
    public AiBrain? Brain { get; private set; }

    public SBasePawn? Pawn => _pawn;

    /// <summary>The team index (0 spies, 1 mercs), from the PRI.</summary>
    public int Team => Controller.PlayerReplicationInfo?.Team is TeamInfo t ? t.TeamIndex : (Controller.Pawn is SPAWNDEFENSE ? 1 : 0);

    /// <summary>The input written last tick.</summary>
    public AiInput LastInput { get; private set; }

    /// <summary>Ticks the bot had a pawn.</summary>
    public long ThinkTicks { get; private set; }

    /// <summary>Last time the pawn lost health, and how much.</summary>
    public float HurtAt { get; private set; } = -100f;

    public int HurtAmount { get; private set; }

    /// <summary>HUD messages received since the brain last read them.</summary>
    public List<AiMessage> Inbox => _messages;

    /// <summary>
    /// Held by a test: the bot writes idle input (no keys, the view kept) and neither perceives nor decides until
    /// released.
    /// </summary>
    public bool Held { get; set; }

    /// <summary>The bot's team play (revive, heal, unspy, co-op moves), asked before the brain.</summary>
    public TeamAi TeamPlay { get; }

    /// <summary>A spy bot's play around the merc's motion tracker (Evasion.cs), applied after the brain.</summary>
    public SpyEvasion Evasion { get; }

    /// <summary>A spy bot's gadgets: smoke, flash-bang, chaff, heartbeat, sticky cam (SpyGadgets.cs), after the brain.</summary>
    public SpyGadgets Gadgets { get; }

    /// <summary>A merc bot's motion tracking: what its goggle tracker shows (AiPerception.cs).</summary>
    public AiMotionSense Motion { get; } = new();

    private bool _teamHeld;

    /// <summary>A one-line state for diagnostics.</summary>
    public string Status => Brain is null ? "no pawn" : TeamPlay.Goal == TeamGoal.None ? Brain.Status : TeamPlay.Status + " | " + Brain.Status;

    /// <summary>The bot's part of a tick: perceive, decide, write the input. Call before the world steps.</summary>
    public void Tick()
    {
        World.BeginTick();
        float now = World.Now;
        float dt = _lastTick < 0f ? Sim.DeltaTime : SvmMath.Clamp(now - _lastTick, 0f, 0.25f);
        _lastTick = now;
        World.Messages.Take(Controller, _messages);
        if (Held)
        {
            Apply(AiInput.Idle);
            return;
        }

        if (Controller.bDeleteMe || Controller.Pawn is not SBasePawn { bDeleteMe: false } pawn || pawn.Health <= 0)
        {
            if (_pawn is not null)
            {
                Brain?.Reset();
                World.Teams[Team & 1].Release(this);
                _pawn = null;
            }

            Memory.Decay(dt, Difficulty);
            Apply(AiInput.Idle);
            return;
        }

        if (!ReferenceEquals(pawn, _pawn))
        {
            _pawn = pawn;
            EnsureGoggle(pawn);
            _lastHealth = pawn.Health;
            Memory.Clear();
            if (Brain is null || (pawn is SPawnAttaque) != (Brain is SpyAi))
            {
                Brain = pawn is SPawnAttaque ? new SpyAi(this) : new MercAi(this);
            }
            else
            {
                Brain.Reset();
            }
        }

        if (pawn.Health < _lastHealth)
        {
            HurtAt = now;
            HurtAmount = _lastHealth - pawn.Health;
        }

        _lastHealth = pawn.Health;
        ThinkTicks++;
        Perceive(pawn, now, dt);
        var plan = new AiPlan { Tolerance = 9000 };
        // Team play first (TeamAi.cs): while it helps a mate the side's brain waits, and plans afresh after.
        if (TeamPlay.Think(now, ref plan))
        {
            _teamHeld = true;
        }
        else
        {
            if (_teamHeld)
            {
                _teamHeld = false;
                Brain!.Reset();
            }

            Brain!.Think(now, ref plan);
        }

        if (pawn is SPawnAttaque)
        {
            Gadgets.Apply(ref plan, now);
            Evasion.Apply(ref plan, now);
        }

        AiInput input = Turn(plan, dt);
        Stance(pawn, ref input, plan.Crouched, now);
        Apply(input);
        GoggleClip(now);
    }

    private static readonly Name NameGoggleUp = new("GoggleUp");
    private static readonly Name NameGoggleDown = new("GoggleDown");
    private float _goggleWaitSince = -1f;

    /// <summary>
    /// The goggle clip's notify. A mode starts when the clip that lowers the goggles reaches its notify
    /// (AnimEvent_GoggleSwitchDown) and ends at the raise clip's (AnimEvent_GoggleSwitchUp); the port raises no
    /// animation notifies yet, so for a bot the notify comes <see cref="AiK.GoggleClipSeconds"/> after the switch.
    /// </summary>
    private void GoggleClip(float now)
    {
        if (Controller.Goggle is not { } goggle)
        {
            return;
        }

        bool waitDown = goggle.IsInState(NameGoggleUp) && goggle.CurrentMode != SGoggle.REN_DynLight && Controller.RendMap != goggle.CurrentMode;
        bool waitUp = goggle.IsInState(NameGoggleDown) && goggle.CurrentMode == SGoggle.REN_DynLight && Controller.RendMap != SGoggle.REN_DynLight;
        if (!waitDown && !waitUp)
        {
            _goggleWaitSince = -1f;
            return;
        }

        if (_goggleWaitSince < 0f)
        {
            _goggleWaitSince = now;
        }

        if (now - _goggleWaitSince < AiK.GoggleClipSeconds)
        {
            return;
        }

        _goggleWaitSince = -1f;
        if (waitDown)
        {
            goggle.eventDown();
        }
        else
        {
            goggle.eventUp();
        }
    }

    /// <summary>
    /// The goggles a player's own machine spawns for its controller (vision-goggles.md rule 1). A bot's machine is the
    /// server, where the game spawns none on a dedicated host; without them the goggle keys do nothing, so the bot's
    /// controller gets them here, as a listen host's own player has them.
    /// </summary>
    private void EnsureGoggle(SBasePawn pawn)
    {
        if (Controller.Goggle is { } own)
        {
            if (!ReferenceEquals(own.Base, pawn))
            {
                own.SetBase(pawn);
            }

            return;
        }

        if (Controller.XLevel is not { } level)
        {
            return;
        }

        if (level.SpawnActor(new SGoggle(), Controller.Location, Controller.Rotation, Controller) is SGoggle goggle)
        {
            goggle.SPC = Controller;
            goggle.SetBase(pawn);
            Controller.Goggle = goggle;
        }
    }

    private void Apply(AiInput input)
    {
        input.ApplyTo(Controller);
        LastInput = input;
    }

    // ---- perception ------------------------------------------------------------------------------------------

    /// <summary>The living enemy pawns (their existence is public: the scoreboard lists the players).</summary>
    public List<Pawn> Enemies
    {
        get
        {
            _enemies.Clear();
            bool spy = _pawn is SPawnAttaque;
            foreach (SBasePawn p in World.Level.DynamicActors<SBasePawn>())
            {
                if (!p.bDeleteMe && p.Health > 0 && !ReferenceEquals(p, _pawn) && (p is SPawnAttaque) != spy)
                {
                    _enemies.Add(p);
                }
            }

            return _enemies;
        }
    }

    private void Perceive(SBasePawn pawn, float now, float dt)
    {
        FVector eye = AiSight.Eye(pawn);
        bool look = now >= _nextSight;
        if (look)
        {
            _nextSight = now + AiK.SightSeconds;
            AiVisionMode mode = AiSight.ModeOf(Controller);
            if (mode == AiVisionMode.Motion)
            {
                Motion.Update(Controller, eye, Controller.Rotation, Controller.FovAngle, now);
            }

            // Through a sticky cam the screen shows the cam's picture: the spy sees from the cam, not its eye.
            FVector sightEye = eye;
            FRotator sightView = Controller.Rotation;
            if (pawn is SPawnAttaque && Gadgets.CamView(out FVector camEye, out FRotator camView))
            {
                sightEye = camEye;
                sightView = camView;
            }

            foreach (Pawn enemy in Enemies)
            {
                AiSighting s = AiSight.Look(World, sightEye, sightView, Controller.FovAngle, mode, enemy, Difficulty);
                bool bodyVisible = s.Visible;
                if (mode == AiVisionMode.Motion)
                {
                    AiSighting tracked = Motion.Look(Controller, eye, Controller.Rotation, Controller.FovAngle, enemy);
                    if (tracked.Strength > s.Strength)
                    {
                        s = tracked;
                    }
                }
                AiContact c = Memory.Observe(enemy, s, now, AiK.SightSeconds, Difficulty, bodyVisible);
                if (c.Visible && c.Confirmed)
                {
                    World.Teams[Team & 1].Call(enemy, c.Position, now, Difficulty.Teamwork, this);
                }
            }

            Memory.EndLook(now);
        }

        // Hearing: every noise of the last tick that reaches this pawn and is not its own side's.
        IReadOnlyList<AiNoise> noises = World.Noise.Noises;
        for (int i = 0; i < noises.Count; i++)
        {
            AiNoise noise = noises[i];
            if (noise.Instigator is not { } who || ReferenceEquals(who, pawn) || who.Health <= 0 || (who is SPawnAttaque) == (pawn is SPawnAttaque))
            {
                continue;
            }

            float heard = AiNoiseBoard.Heard(World, in noise, eye, Difficulty);
            if (heard > 0f)
            {
                // A heard noise gives a position as good as the ear: blurred with distance.
                float blur = SvmMath.Min(300f, (noise.Location - eye).Size() * 0.15f) * (1f - heard);
                FVector guess = noise.Location + new FVector(Random.RandRange(-blur, blur), Random.RandRange(-blur, blur), 0f);
                Memory.Report(who, guess, now, SvmMath.Clamp(0.35f + 0.5f * heard, 0f, 0.85f), AiContactSource.Heard);
            }
        }

        // Teammates' callouts arrive with the teamwork knob's weight.
        if (Difficulty.Teamwork > 0f)
        {
            IReadOnlyList<AiTeamBoard.Callout> calls = World.Teams[Team & 1].Callouts;
            for (int i = 0; i < calls.Count; i++)
            {
                AiTeamBoard.Callout call = calls[i];
                if (!ReferenceEquals(call.From, this) && now - call.Time < 2f + 3f * Difficulty.Teamwork &&
                    now - call.Time > 0.6f * (1f - Difficulty.Teamwork))
                {
                    Memory.Report(call.Enemy, call.Position, call.Time, 0.7f * Difficulty.Teamwork, AiContactSource.Told);
                }
            }
        }

        Memory.Decay(dt, Difficulty);
    }

    // ---- view and stance -------------------------------------------------------------------------------------

    private AiInput Turn(AiPlan plan, float dt)
    {
        AiInput input = plan.Keys;
        FRotator view = Controller.Rotation;
        int maxStep = Math.Max(1, (int)(Difficulty.TurnRateDegrees * (65536f / 360f) * SvmMath.Max(dt, Sim.DeltaTime)));
        int yaw = view.Yaw & 0xFFFF;
        int pitch = view.Pitch;
        if (plan.Yaw is { } wantYaw)
        {
            int d = ((wantYaw - yaw + 32768) & 0xFFFF) - 32768;
            yaw = (yaw + Math.Clamp(d, -maxStep, maxStep)) & 0xFFFF;
        }

        if (plan.Pitch is { } wantPitch)
        {
            int cur = ((pitch + 32768) & 0xFFFF) - 32768;
            int want = ((wantPitch + 32768) & 0xFFFF) - 32768;
            pitch = (cur + Math.Clamp(want - cur, -maxStep, maxStep)) & 0xFFFF;
        }
        else
        {
            // Level the view back out while walking.
            int cur = ((pitch + 32768) & 0xFFFF) - 32768;
            pitch = (cur + Math.Clamp(-cur, -maxStep / 2, maxStep / 2)) & 0xFFFF;
        }

        var newView = new FRotator(pitch, yaw, 0);
        input.View = newView;
        input.Aim = newView;
        if (plan.MoveYaw is { } move && input.BT_Up != 0)
        {
            // Eight-way keys for the move direction measured from the view.
            int rel = ((move - yaw + 32768) & 0xFFFF) - 32768;
            input.BT_Up = (byte)(Math.Abs(rel) < 12288 ? 1 : 0);
            input.BT_Down = (byte)(Math.Abs(rel) > 20480 ? 1 : 0);
            input.BT_Right = (byte)(rel > 4096 && rel < 28672 ? 1 : 0);
            input.BT_Left = (byte)(rel < -4096 && rel > -28672 ? 1 : 0);
            return input;
        }
        if (plan.Yaw is { } target && NavFollower.YawError(yaw, target) > plan.Tolerance)
        {
            // Turn before moving: the keys are relative to the view.
            input.BT_Up = 0;
            input.BT_Down = 0;
            input.BT_Left = 0;
            input.BT_Right = 0;
        }

        return input;
    }

    private void Stance(SBasePawn pawn, ref AiInput input, bool? crouched, float now)
    {
        if (_crouchFrames > 0)
        {
            input.bCrouch = 1;
            _crouchFrames--;
            return;
        }

        if (crouched is not { } want || pawn.Physics != EPhysics.PHYS_Walking || pawn.bIsCrouched == want || now - _crouchAt < 0.6f)
        {
            return;
        }

        // The crouch key toggles: hold it three ticks.
        _crouchAt = now;
        _crouchFrames = 2;
        input.bCrouch = 1;
    }
}
