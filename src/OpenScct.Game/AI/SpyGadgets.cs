using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using EK = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.AI;

/// <summary>The gadget a spy bot is using (diagnostics and tests).</summary>
public enum SpyGadgetAct : byte
{
    None,
    Smoke,
    Flash,
    Chaff,
    Heartbeat,
    StickyCam,
}

/// <summary>
/// A spy bot's gadgets, played after the brain from what the bot knows (its memory, its own screen):
/// <list type="bullet">
/// <item><b>Smoke</b>: quick-launched at the feet when a merc shoots it or faces it close, or to cover the last steps
/// to a terminal with a merc about; thrown at a guard (it pops on him, he coughs, and faints after 6 s inside).</item>
/// <item><b>Flash-bang</b>: at a merc that faces the spy at close to mid range: quick-launched at the feet when
/// holstered (it pops at once and flashes every merc within 2500 uu that faces it with an open line), thrown at him
/// when the Shocker is out.</item>
/// <item><b>Chaff</b>: thrown at a merc guarding the target terminal, or quick-launched at the feet when a merc closes
/// in: within 300 uu of the burst his goggles go back to normal vision and his HUD is jammed for as long as he stays,
/// and his torch fails for 5 s.</item>
/// <item><b>Heartbeat</b>: on the way to a terminal, or when the bot does not know where the mercs are, the sensor is
/// selected and the Shocker drawn for a short sweep; what the radar shows (every merc within 2200 uu in a 60 degree
/// cone of the view, through walls) goes into the memory.</item>
/// <item><b>Sticky cam</b>: shot onto a wall near a guard so that the gas point (192 uu in front of the cam, which
/// faces the spy after the entry) is where he stands; the bot watches through the cam (its sight then comes from the
/// cam, see <see cref="CamView"/>) and releases the gas when a merc comes within reach, then leaves the view.</item>
/// </list>
/// Everything goes through the keys: GadgetQuickChoice (holstered), SelectGadget, the fire-mode key to draw and
/// holster, AltFire for an aimed throw or a cam, Use for the gas. The difficulty's GadgetUse sets how readily and how
/// carefully (aim tolerance) the gadgets are used; below 0.3 only the quick launches are used.
/// </summary>
public sealed class SpyGadgets
{
    private enum Step : byte
    {
        Idle,
        Quick,
        Select,
        Draw,
        Aim,
        Release,
        Scan,
        CamWait,
        CamView,
        CamLeave,
        Holster,
    }

    private readonly AiController _bot;
    private readonly List<(Pawn Pawn, float Until)> _blinded = new();
    private SBasePawn? _pawn;
    private SpyGadgetAct _act;
    private Step _step;
    private float _stepAt;
    private float _actAt;
    private SGadget? _gadget;
    private int _slot;
    private Pawn? _target;
    private FVector _aimPoint;
    private FRotator _aimRot;
    private bool _straight;
    private float _lastUsedBefore;
    private bool _drew;
    private bool _pressed;
    private bool _retried;
    private bool _unsure;
    private int _scanBaseYaw;
    private float _nextCheck;
    private float _nextSmoke;
    private float _nextFlash;
    private float _nextChaff;
    private float _nextScan;
    private float _nextCam;
    private float _nextGuard;
    private float _gassedAt = -1f;
    private bool _usingHeld;
    private SComputerObjectiveTrigger? _coveredTerminal;

    public SpyGadgets(AiController bot)
    {
        _bot = bot ?? throw new ArgumentNullException(nameof(bot));
    }

    /// <summary>The gadget in use now.</summary>
    public SpyGadgetAct Active => _act;

    public int Smokes { get; private set; }

    public int Flashes { get; private set; }

    public int Chaffs { get; private set; }

    public int Scans { get; private set; }

    public int CamsPlaced { get; private set; }

    public int Gassings { get; private set; }

    /// <summary>True while the bot looks through a sticky cam (its screen is the cam's picture).</summary>
    public bool InCamView => _bot.Pawn is SPawnAttaque { UsedObject: SStickyCam { bDeleteMe: false } } &&
        GadgetStates.InStickyCamView(_bot.Controller);

    /// <summary>
    /// The view the bot's screen shows while it looks through its sticky cam: the cam's place and rotation. False
    /// outside the cam view (the pawn's own eye then).
    /// </summary>
    public bool CamView(out FVector eye, out FRotator view)
    {
        if (InCamView && _bot.Pawn!.UsedObject is SStickyCam cam)
        {
            eye = cam.Location;
            view = cam.Rotation;
            return true;
        }

        eye = default;
        view = default;
        return false;
    }

    /// <summary>True for a merc this bot flash-banged in the last few seconds (a moment to flee or close in).</summary>
    public bool IsBlinded(Pawn merc, float now)
    {
        foreach ((Pawn p, float until) in _blinded)
        {
            if (ReferenceEquals(p, merc) && now < until)
            {
                return true;
            }
        }

        return false;
    }

    public void Reset()
    {
        _act = SpyGadgetAct.None;
        _step = Step.Idle;
        _gadget = null;
        _target = null;
        _drew = false;
        _usingHeld = false;
        _gassedAt = -1f;
        _blinded.Clear();
        _coveredTerminal = null;
    }

    /// <summary>Adjusts the brain's plan for this tick: carries on with a gadget in use, or picks one when due.</summary>
    public void Apply(ref AiPlan plan, float now)
    {
        if (_bot.Pawn is not SPawnAttaque spy || _bot.Brain is not SpyAi brain)
        {
            _act = SpyGadgetAct.None;
            return;
        }

        if (!ReferenceEquals(spy, _pawn))
        {
            _pawn = spy;
            Reset();
        }

        if (_act != SpyGadgetAct.None)
        {
            Continue(spy, brain, now, ref plan);
            return;
        }

        if (now < _nextCheck)
        {
            return;
        }

        _nextCheck = now + SpyGadgetK.CheckSeconds;
        if (CanStart(spy, brain))
        {
            Decide(spy, brain, now, ref plan);
        }
    }

    // ---- choice ------------------------------------------------------------------------------------------------

    private bool CanStart(SBasePawn spy, SpyAi brain) =>
        _bot.Difficulty.GadgetUse >= SpyGadgetK.MinGadgetUse && spy.Physics == EPhysics.PHYS_Walking && spy.Health > 0 &&
        brain.Goal != SpyAi.GShock && !brain.AtTerminal && !brain.InMelee && _bot.TeamPlay.Goal == TeamGoal.None && !spy.bStickyShocking &&
        _bot.Controller.iDontUpdatePadInput == 0 && !GadgetStates.InStickyCamView(_bot.Controller);

    private bool Roll(float chance) => _bot.Random.FRand() < chance;

    private void Decide(SBasePawn spy, SpyAi brain, float now, ref AiPlan plan)
    {
        float use = _bot.Difficulty.GadgetUse;
        bool holstered = spy.WeaponLoaded == 0;
        bool hurt = now - _bot.HurtAt < 1.5f;
        bool careful = use >= SpyGadgetK.AimedGadgetUse;
        Pawn? merc = NearestSeen(spy, out float dist, out bool facing);

        // A merc facing the spy at close to mid range: flash him.
        if (merc is not null && facing && dist > SpyGadgetK.FlashMin && dist < SpyGadgetK.FlashMax && !IsBlinded(merc, now) &&
            now >= _nextFlash && Roll(use * (hurt ? 0.9f : 0.6f)) && Slot(spy, out SGadFlashBang? flash) is >= 0 and var fs &&
            Ready(flash!, now))
        {
            if (holstered && QuickReady(now) && _bot.World.LineOpen(spy.Location, merc.Location))
            {
                StartQuick(SpyGadgetAct.Flash, fs, flash!, merc, now);
                return;
            }

            if (!holstered && careful)
            {
                StartAimed(SpyGadgetAct.Flash, fs, flash!, merc, merc.Location, now);
                return;
            }
        }

        // Shot at, or a merc facing it close: smoke at the feet to break the line.
        if ((hurt || (merc is not null && facing && dist < SpyGadgetK.SmokeDefendRange)) && holstered && QuickReady(now) &&
            now >= _nextSmoke && !_bot.World.InSmoke(spy.Location) && Roll(use * 0.7f) &&
            Slot(spy, out SGadSmokeGrenade? smoke) is >= 0 and var ss && Ready(smoke!, now))
        {
            StartQuick(SpyGadgetAct.Smoke, ss, smoke!, merc, now);
            return;
        }

        // A merc closing in: chaff at the feet jams him when he comes within 300 uu.
        if (merc is not null && dist < SpyGadgetK.ChaffCloseRange && holstered && QuickReady(now) && now >= _nextChaff &&
            Closing(merc, spy) && Roll(use * 0.5f) && Slot(spy, out SGadChaffeGrenade? chaff) is >= 0 and var cs && Ready(chaff!, now))
        {
            StartQuick(SpyGadgetAct.Chaff, cs, chaff!, merc, now);
            return;
        }

        if (brain.Goal is SpyAi.GHack or SpyAi.GIdle && merc is null || brain.Goal == SpyAi.GHack && merc is not null && !facing)
        {
            if (GuardPlay(spy, brain, now, careful, holstered))
            {
                return;
            }
        }

        // The last steps to a terminal with a merc about: a smoke at the feet covers the hack.
        if (brain.Goal == SpyAi.GHack && brain.Target is { } t && !ReferenceEquals(t, _coveredTerminal) &&
            (t.Location - spy.Location).Size() < SpyGadgetK.CoverHackRange && holstered && QuickReady(now) && now >= _nextSmoke &&
            !_bot.World.InSmoke(spy.Location) && MercKnownWithin(spy.Location, SpyGadgetK.CoverHackMercRange, 0.4f) &&
            Roll(use * 0.5f) && Slot(spy, out SGadSmokeGrenade? cover) is >= 0 and var cv && Ready(cover!, now))
        {
            _coveredTerminal = t;
            StartQuick(SpyGadgetAct.Smoke, cv, cover!, null, now);
            return;
        }

        // The heartbeat on the way to a terminal, or when the bot does not know where the mercs are.
        if (careful && merc is null && now >= _nextScan && brain.Goal is SpyAi.GHack or SpyAi.GIdle &&
            Slot(spy, out SGadHeartBeatSensor? hb) is >= 0 and var hs)
        {
            bool unsure = !MercKnownWithin(spy.Location, float.MaxValue, 0.3f);
            float toTarget = brain.Target is { } tt ? (tt.Location - spy.Location).Size() : float.MaxValue;
            if ((unsure || toTarget is > SpyGadgetK.ScanMinRange and < SpyGadgetK.ScanMaxRange) && Roll(use * 0.5f))
            {
                StartScan(hs, hb!, brain, unsure, now);
            }
        }
    }

    /// <summary>
    /// A merc guarding the target terminal (a believed position near it): a sticky cam with gas where he stands, a
    /// chaff grenade at his feet, or a smoke grenade at him.
    /// </summary>
    private bool GuardPlay(SBasePawn spy, SpyAi brain, float now, bool careful, bool holstered)
    {
        if (!careful || now < _nextGuard || brain.Target is not { } terminal)
        {
            return false;
        }

        AiContact? guard = null;
        foreach (AiContact c in _bot.Memory.Contacts)
        {
            if (c.Pawn is SPAWNDEFENSE && c.Confidence > 0.5f && (c.Position - terminal.Location).Size() < SpyGadgetK.GuardRange &&
                (guard is null || c.Confidence > guard.Confidence))
            {
                guard = c;
            }
        }

        if (guard is null)
        {
            return false;
        }

        FVector at = guard.Visible ? guard.Pawn.Location : guard.Position;
        float d = (at - spy.Location).Size();
        if (d < SpyGadgetK.GuardMin || d > SpyGadgetK.GuardMax)
        {
            return false;
        }

        _nextGuard = now + SpyGadgetK.GuardPause;
        float use = _bot.Difficulty.GadgetUse;
        bool moving = guard.Visible && guard.Pawn.Velocity.Size() > 120f;

        // A cam whose gas point lands on the guard's spot.
        if (!moving && now >= _nextCam && Roll(use * 0.6f) && Slot(spy, out SGadStickyCam? cam) is >= 0 and var camSlot &&
            HasCharge(cam!) && Ready(cam!, now) && FindCamSpot(spy, at, out FRotator camAim))
        {
            StartCam(camSlot, cam!, guard.Pawn, camAim, now);
            return true;
        }

        bool line = _bot.World.LineOpen(AiSight.Eye(spy), at);
        if (!line)
        {
            return false;
        }

        if (now >= _nextChaff && Roll(use * 0.5f) && Slot(spy, out SGadChaffeGrenade? chaff) is >= 0 and var chaffSlot && Ready(chaff!, now))
        {
            // At his feet, a little short (it bounces on).
            FVector feet = at - new FVector(0f, 0f, guard.Pawn.CollisionHeight) + (spy.Location - at).SafeNormal() * 60f;
            StartAimed(SpyGadgetAct.Chaff, chaffSlot, chaff!, guard.Pawn, feet, now);
            return true;
        }

        if (guard.Visible && !((SBasePawn)guard.Pawn).bGasMask && now >= _nextSmoke && d < SpyGadgetK.SmokeAtGuardMax &&
            Roll(use * 0.5f) && Slot(spy, out SGadSmokeGrenade? smoke) is >= 0 and var smokeSlot && Ready(smoke!, now))
        {
            StartAimed(SpyGadgetAct.Smoke, smokeSlot, smoke!, guard.Pawn, at, now);
            return true;
        }

        return false;
    }

    /// <summary>The nearest merc the bot sees now (confirmed), and whether it faces the spy.</summary>
    private Pawn? NearestSeen(SBasePawn spy, out float distance, out bool facing)
    {
        Pawn? best = null;
        distance = float.MaxValue;
        facing = false;
        foreach (AiContact c in _bot.Memory.Contacts)
        {
            if (!c.Visible || !c.Confirmed || c.Pawn is not SPAWNDEFENSE)
            {
                continue;
            }

            FVector to = spy.Location - c.Pawn.Location;
            float d = to.Size();
            if (d < distance)
            {
                best = c.Pawn;
                distance = d;
                facing = d < 1f || (to | c.Pawn.Rotation.Vector()) / d > SpyGadgetK.FacingCos;
            }
        }

        return best;
    }

    private static bool Closing(Pawn merc, SBasePawn spy)
    {
        FVector to = (spy.Location - merc.Location).SafeNormal();
        return (merc.Velocity | to) > SpyGadgetK.ClosingSpeed;
    }

    private bool MercKnownWithin(FVector at, float range, float confidence)
    {
        foreach (AiContact c in _bot.Memory.Contacts)
        {
            if (c.Pawn is SPAWNDEFENSE && c.Confidence > confidence && (c.Position - at).Size() < range)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The quick launch is allowed: no fire-mode press pending and 2 s since the last one.</summary>
    private bool QuickReady(float now) =>
        _bot.Controller.fDateFireMode == 0f && now > _bot.Controller.fDateLaunchedLastQuickGadget + EK.QuickLaunchCooldown + 0.1f;

    private static bool HasCharge(SGadget g) => g.Charge > 0 || g.Charge == EK.UnlimitedCharge;

    private static bool Ready(SGadget g, float now) => HasCharge(g) && now - g.LastUsed >= g.RefireRate;

    /// <summary>
    /// The slot (0 to 3, as the gadget keys count) of the first gadget of type <typeparamref name="T"/>, or -1; only
    /// the team's slots count (STeamInfo.iNBGadgets).
    /// </summary>
    private int Slot<T>(SBasePawn pawn, out T? gadget) where T : SGadget
    {
        int slot = 0;
        int slots = PawnInventory.TeamSlots(pawn);
        for (SGadget? g = pawn.MyGadget; g is not null && slot < slots; g = g.MyGadget)
        {
            if (!PawnInventory.IsSelectable(g))
            {
                continue;
            }

            if (g is T t && !g.bDeleteMe)
            {
                gadget = t;
                return slot;
            }

            slot++;
        }

        gadget = null;
        return -1;
    }

    // ---- starting -------------------------------------------------------------------------------------------------

    private void Begin(SpyGadgetAct act, Step step, int slot, SGadget gadget, Pawn? target, float now)
    {
        _act = act;
        _step = step;
        _stepAt = now;
        _actAt = now;
        _slot = slot;
        _gadget = gadget;
        _target = target;
        _drew = false;
        _pressed = false;
        _retried = false;
        _lastUsedBefore = gadget.LastUsed;
    }

    private void StartQuick(SpyGadgetAct act, int slot, SGadget gadget, Pawn? target, float now) =>
        Begin(act, Step.Quick, slot, gadget, target, now);

    private void StartAimed(SpyGadgetAct act, int slot, SGadget gadget, Pawn? target, FVector point, float now)
    {
        Begin(act, Step.Select, slot, gadget, target, now);
        _aimPoint = point;
        _straight = false;
    }

    private void StartScan(int slot, SGadHeartBeatSensor sensor, SpyAi brain, bool unsure, float now)
    {
        Begin(SpyGadgetAct.Heartbeat, Step.Select, slot, sensor, null, now);
        _unsure = unsure;
        FVector eye = AiSight.Eye(_bot.Pawn!);
        _scanBaseYaw = brain.Target is { } t && !unsure ? (t.Location - eye).Rotation().Yaw & 0xFFFF : _bot.Controller.Rotation.Yaw & 0xFFFF;
    }

    private void StartCam(int slot, SGadStickyCam cam, Pawn guard, FRotator aim, float now)
    {
        Begin(SpyGadgetAct.StickyCam, Step.Select, slot, cam, guard, now);
        _aimRot = aim;
        _straight = true;
        _gassedAt = -1f;
    }

    // ---- carrying on ----------------------------------------------------------------------------------------------

    private void Continue(SBasePawn spy, SpyAi brain, float now, ref AiPlan plan)
    {
        if (spy.Health <= 0 || _gadget is null || _gadget.bDeleteMe)
        {
            Finish(now, 1f);
            return;
        }

        bool inCam = GadgetStates.InStickyCamView(_bot.Controller);

        // The shocker, a melee move or a hack takes over (not while the cam view is on: it is left first).
        if (!inCam && _step is not Step.CamLeave and not Step.Holster && (brain.Goal == SpyAi.GShock || brain.AtTerminal || brain.InMelee))
        {
            Finish(now, 1f);
            return;
        }

        if (now - _actAt > Timeout())
        {
            if (inCam)
            {
                Enter(Step.CamLeave, now);
            }
            else if (_step != Step.Holster && _drew)
            {
                Enter(Step.Holster, now);
            }
            else
            {
                Finish(now, 3f);
                return;
            }
        }

        Scrub(ref plan);
        switch (_step)
        {
            case Step.Quick:
                Quick(spy, now, ref plan);
                break;
            case Step.Select:
                Select(spy, now, ref plan);
                break;
            case Step.Draw:
                Draw(spy, now, ref plan);
                break;
            case Step.Aim:
                Aim(spy, now, ref plan);
                break;
            case Step.Release:
                Release(now, ref plan);
                break;
            case Step.Scan:
                Scan(spy, now, ref plan);
                break;
            case Step.CamWait:
                CamWait(spy, now, ref plan);
                break;
            case Step.CamView:
                CamWatch(spy, now, ref plan);
                break;
            case Step.CamLeave:
                CamLeave(now, ref plan);
                break;
            case Step.Holster:
                Holster(spy, now, ref plan);
                break;
        }
    }

    private float Timeout() => _act switch
    {
        SpyGadgetAct.StickyCam => SpyGadgetK.CamSetupSeconds + SpyGadgetK.CamWatchSeconds,
        SpyGadgetAct.Heartbeat => SpyGadgetK.ScanSeconds + 4f,
        _ => 5f,
    };

    private void Enter(Step step, float now)
    {
        _step = step;
        _stepAt = now;
        _pressed = false;
        _retried = false;
    }

    /// <summary>The gadget is done: a pause before the same kind again (longer after a failure).</summary>
    private void Finish(float now, float pause)
    {
        switch (_act)
        {
            case SpyGadgetAct.Smoke:
                _nextSmoke = now + pause;
                break;
            case SpyGadgetAct.Flash:
                _nextFlash = now + pause;
                break;
            case SpyGadgetAct.Chaff:
                _nextChaff = now + pause;
                break;
            case SpyGadgetAct.Heartbeat:
                _nextScan = now + pause;
                break;
            case SpyGadgetAct.StickyCam:
                _nextCam = now + pause;
                break;
        }

        // A route followed while standing still for a while is planned afresh.
        if (now - _actAt > 1f)
        {
            _bot.Brain?.Follower.Clear();
        }

        _act = SpyGadgetAct.None;
        _step = Step.Idle;
        _gadget = null;
        _target = null;
        _drew = false;
        _usingHeld = false;
    }

    /// <summary>The brain's gadget keys are dropped while a gadget is in use (this class owns them then).</summary>
    private static void Scrub(ref AiPlan plan)
    {
        plan.Keys.bFireMode = 0;
        plan.Keys.bAltFire = 0;
        plan.Keys.bFire = 0;
        if (IsGadgetCommand(plan.Keys.Command2))
        {
            plan.Keys.Command2 = AiCommand.None;
        }

        if (IsGadgetCommand(plan.Keys.Command1))
        {
            plan.Keys.Command1 = plan.Keys.Command2;
            plan.Keys.Arg1 = plan.Keys.Arg2;
            plan.Keys.Command2 = AiCommand.None;
        }
    }

    private static bool IsGadgetCommand(AiCommand c) =>
        c is AiCommand.Fire or AiCommand.AltFire or AiCommand.GadgetQuickChoice or AiCommand.SelectGadget;

    private static void StandStill(ref AiPlan plan)
    {
        plan.Keys.BT_Up = 0;
        plan.Keys.BT_Down = 0;
        plan.Keys.BT_Left = 0;
        plan.Keys.BT_Right = 0;
        plan.MoveYaw = null;
        plan.Crouched = null;
    }

    /// <summary>The quick launch (GadgetQuickChoice, holstered): stand still until the grenade leaves the hand.</summary>
    private void Quick(SBasePawn spy, float now, ref AiPlan plan)
    {
        StandStill(ref plan);
        if (_target is not null)
        {
            LookAt(_target.Location, ref plan);
        }

        if (!_pressed)
        {
            plan.Keys.Press(AiCommand.GadgetQuickChoice, _slot);
            _pressed = true;
            return;
        }

        if (Thrown())
        {
            Count();
            Finish(now, Pause());
            return;
        }

        if (now - _stepAt > SpyGadgetK.QuickWaitSeconds || spy.WeaponLoaded != 0)
        {
            Finish(now, 2f);
        }
    }

    /// <summary>SelectGadget on the slot until it is the selected one.</summary>
    private void Select(SBasePawn spy, float now, ref AiPlan plan)
    {
        if (ReferenceEquals(spy.SelectedGadget, _gadget))
        {
            Enter(Step.Draw, now);
            Draw(spy, now, ref plan);
            return;
        }

        if (now - _stepAt > 0.8f)
        {
            Finish(now, 3f);
            return;
        }

        // One press, and once more if the first was refused.
        if (!_pressed || now - _stepAt > 0.4f && _gadget is not null && !_retried)
        {
            _retried = _pressed;
            _pressed = true;
            plan.Keys.Press(AiCommand.SelectGadget, _slot);
        }
    }

    /// <summary>The Shocker out: the fire-mode key held past half a second (no laser), then wait for the draw.</summary>
    [Provenance(ProvenanceKind.Decoded, "spy-shocker.md rule 1: a 0.5 s hold draws without the laser")]
    private void Draw(SBasePawn spy, float now, ref AiPlan plan)
    {
        if (spy.WeaponLoaded != 0 && !spy.bWeaponTransit)
        {
            Enter(_act == SpyGadgetAct.Heartbeat ? Step.Scan : Step.Aim, now);
            return;
        }

        if (spy.WeaponLoaded == 0 && now - _stepAt < EvasionK.HoldDrawSeconds)
        {
            plan.Keys.bFireMode = 1;
            _drew = true;
        }

        if (_act != SpyGadgetAct.Heartbeat)
        {
            StandStill(ref plan);
        }

        if (now - _stepAt > 2f)
        {
            Enter(Step.Holster, now);
        }
    }

    /// <summary>Turns to the aim and lets fly once it is close enough (a careless bot fires sooner).</summary>
    private void Aim(SBasePawn spy, float now, ref AiPlan plan)
    {
        StandStill(ref plan);
        if (!ReferenceEquals(spy.SelectedGadget, _gadget))
        {
            Enter(Step.Holster, now);
            return;
        }

        FRotator aim = _straight ? _aimRot : ThrowAim(spy);
        plan.Yaw = aim.Yaw & 0xFFFF;
        plan.Pitch = aim.Pitch;
        plan.Tolerance = 65536;
        FRotator view = _bot.Controller.Rotation;
        int yawErr = NavFollower.YawError(view.Yaw & 0xFFFF, aim.Yaw & 0xFFFF);
        int pitchErr = Math.Abs(Wrap(view.Pitch) - Wrap(aim.Pitch));
        float tolerance = (_straight ? SpyGadgetK.CamAimDegrees : SpyGadgetK.ThrowAimDegrees + (1f - _bot.Difficulty.GadgetUse) * 3f) * (65536f / 360f);
        if (yawErr <= tolerance && pitchErr <= tolerance || now - _stepAt > 1.5f)
        {
            _lastUsedBefore = _gadget!.LastUsed;
            plan.Keys.Press(AiCommand.AltFire);
            plan.Keys.bAltFire = _straight ? (byte)0 : (byte)1;
            Enter(Step.Release, now);
        }
    }

    /// <summary>The key let go (a tap under 0.25 s throws at full strength); a cam goes on the press.</summary>
    private void Release(float now, ref AiPlan plan)
    {
        StandStill(ref plan);
        plan.Yaw = null;
        plan.Pitch = _bot.Controller.Rotation.Pitch;
        if (Thrown())
        {
            Count();
            if (_act == SpyGadgetAct.StickyCam)
            {
                Enter(Step.CamWait, now);
                return;
            }

            Enter(Step.Holster, now);
            return;
        }

        if (now - _stepAt > 0.6f)
        {
            if (_act == SpyGadgetAct.StickyCam)
            {
                _nextCam = now + 15f;
            }

            Enter(Step.Holster, now);
        }
    }

    /// <summary>The heartbeat sweep: the view turns over the cone while the walk goes on; the radar feeds memory.</summary>
    private void Scan(SBasePawn spy, float now, ref AiPlan plan)
    {
        float t = now - _stepAt;
        float length = _unsure ? SpyGadgetK.ScanSeconds * 1.5f : SpyGadgetK.ScanSeconds;
        if (t > length || !ReferenceEquals(spy.SelectedGadget, _gadget) || spy.WeaponLoaded == 0)
        {
            Scans++;
            Enter(Step.Holster, now);
            return;
        }

        int sweep = _unsure ? (int)(65536f * t / length) : (int)((t / length * 2f - 1f) * SpyGadgetK.ScanHalfSweep);
        if (plan.Keys.BT_Up != 0 && plan.Yaw is { } moving && plan.MoveYaw is null)
        {
            plan.MoveYaw = moving;
        }

        plan.Yaw = (_scanBaseYaw + sweep) & 0xFFFF;
        plan.Pitch = 0;
        plan.Tolerance = 65536;
        Sense(spy, now);
    }

    /// <summary>
    /// What the sensor's radar shows: every merc within its range in the cone of the view (no wall or team test,
    /// spec/equipment/heartbeat-sensor.md rule 2), as a position on the radar.
    /// </summary>
    private void Sense(SBasePawn spy, float now)
    {
        if (_gadget is not SGadHeartBeatSensor sensor)
        {
            return;
        }

        FVector view = _bot.Controller.Rotation.Vector();
        float range2 = sensor.fDetectionDistance * sensor.fDetectionDistance;
        foreach (Pawn p in _bot.Enemies)
        {
            if (p is not SPAWNDEFENSE)
            {
                continue;
            }

            FVector d = p.Location - spy.Location;
            if (d.SizeSquared() >= range2 || (view | d.SafeNormal()) <= sensor.fDemiAngle)
            {
                continue;
            }

            // The radar's scale blurs a little with distance.
            float blur = d.Size() * SpyGadgetK.RadarBlur;
            FVector at = p.Location + new FVector(_bot.Random.RandRange(-blur, blur), _bot.Random.RandRange(-blur, blur), 0f);
            _bot.Memory.Report(p, at, now, SpyGadgetK.SensorConfidence, AiContactSource.Sensed);
        }
    }

    /// <summary>
    /// The cam fired: wait for the cam view. The smooth entry ends in the camera code of the player's machine when the
    /// view reaches the cam (within 80 uu at 2000 uu/s); a server bot draws no camera, so the arrival comes after that
    /// flight time.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "PlayerCalcView.SmoothToStickyView (camera.md rule 4.4): 2000 uu/s, arrival within 80 uu")]
    private void CamWait(SBasePawn spy, float now, ref AiPlan plan)
    {
        StandStill(ref plan);
        plan.Yaw = null;
        SPlayerController pc = _bot.Controller;
        if (pc.IsInState(GadgetStates.s_UseObject_StickyCam))
        {
            Enter(Step.CamView, now);
            return;
        }

        if (pc.IsInState(GadgetStates.s_UseObject_StickyCam_SmoothToPosition) && spy.UsedObject is SStickyCam cam)
        {
            float flight = SvmMath.Max(0.1f, ((cam.Location - AiSight.Eye(spy)).Size() - 80f) / 2000f);
            if (now - _stepAt > flight)
            {
                pc.eventChangeState(GadgetStates.s_UseObject_StickyCam, Name.None, true);
            }

            return;
        }

        if (now - _stepAt > 1.5f)
        {
            // No cam (it did not stick, or the server did not enter the view).
            _nextCam = now + 15f;
            Enter(Step.Holster, now);
        }
    }

    /// <summary>
    /// Watching through the cam: the gas when a merc comes within reach of the gas point (192 uu in front of the cam,
    /// 128 uu round it; the cloud keeps catching for 6 s), then out of the view.
    /// </summary>
    private void CamWatch(SBasePawn spy, float now, ref AiPlan plan)
    {
        plan.Keys = default;
        plan.Yaw = null;
        plan.Pitch = null;
        plan.MoveYaw = null;
        plan.Crouched = null;
        if (!InCamView || spy.UsedObject is not SStickyCam cam)
        {
            Finish(now, 6f);
            return;
        }

        bool leave = now - _stepAt > SpyGadgetK.CamWatchSeconds * (0.5f + 0.5f * _bot.Difficulty.GadgetUse) ||
            _gassedAt >= 0f && now - _gassedAt > 1.5f || now - _bot.HurtAt < 0.5f;
        FVector gas = cam.Location + cam.Rotation.Vector() * EK.StickyGasAhead;
        foreach (AiContact c in _bot.Memory.Contacts)
        {
            if (c.Pawn is not SPAWNDEFENSE m)
            {
                continue;
            }

            // A merc coming for the spy's body: out of the view.
            if (c.Confidence > 0.5f && (c.Position - spy.Location).Size() < SpyGadgetK.CamLeaveRange)
            {
                leave = true;
            }

            bool fresh = c.Visible || c.Confidence > 0.6f && now - c.Time < 0.5f;
            if (!fresh || cam.Smoke_NbDoses <= 0 || now < cam.NextUseTime || c.Visible && m.bGasMask)
            {
                continue;
            }

            FVector at = c.Visible ? m.Location + m.Velocity * SpyGadgetK.GasLead : c.Position;
            if ((at - gas).Size() < SpyGadgetK.GasReach && _bot.World.LineOpen(cam.Location, at) && !_usingHeld)
            {
                plan.Keys.bUsing = 1;
                _gassedAt = now;
                Gassings++;
                break;
            }
        }

        _usingHeld = plan.Keys.bUsing != 0;
        if (leave && plan.Keys.bUsing == 0)
        {
            Enter(Step.CamLeave, now);
            plan.Keys.Press(AiCommand.AltFire);
        }
    }

    /// <summary>Alt-fire leaves the view; the leave state then holsters the pistol itself.</summary>
    private void CamLeave(float now, ref AiPlan plan)
    {
        plan.Keys = default;
        plan.Yaw = null;
        plan.Pitch = null;
        plan.MoveYaw = null;
        plan.Crouched = null;
        if (!GadgetStates.InStickyCamView(_bot.Controller))
        {
            _drew = false;
            Finish(now, _gassedAt >= 0f ? 20f : 10f);
            return;
        }

        if (((int)((now - _stepAt) / 0.4f) & 1) == 1 || now - _stepAt > 3f)
        {
            plan.Keys.Press(AiCommand.AltFire);
        }
    }

    /// <summary>Holsters a Shocker this class drew (one tap of the fire-mode key), then done.</summary>
    private void Holster(SBasePawn spy, float now, ref AiPlan plan)
    {
        plan.Yaw = null;
        if (!_drew || spy.WeaponLoaded == 0)
        {
            Finish(now, Pause());
            return;
        }

        if (!_pressed && !spy.bWeaponTransit)
        {
            plan.Keys.bFireMode = 1;
            _pressed = true;
            return;
        }

        if (now - _stepAt > 1f)
        {
            Finish(now, Pause());
        }
    }

    private float Pause() => _act switch
    {
        SpyGadgetAct.Heartbeat => SpyGadgetK.ScanEvery + (1f - _bot.Difficulty.GadgetUse) * SpyGadgetK.ScanEvery,
        SpyGadgetAct.StickyCam => 20f,
        _ => 4f + (1f - _bot.Difficulty.GadgetUse) * 6f,
    };

    private bool Thrown() => _gadget is not null && SvmMath.Abs(_gadget.LastUsed - _lastUsedBefore) > 0.0001f;

    private void Count()
    {
        switch (_act)
        {
            case SpyGadgetAct.Smoke:
                Smokes++;
                break;
            case SpyGadgetAct.Chaff:
                Chaffs++;
                break;
            case SpyGadgetAct.Flash:
                Flashes++;
                if (_target is not null)
                {
                    _blinded.RemoveAll(b => ReferenceEquals(b.Pawn, _target) || _bot.World.Now >= b.Until);
                    _blinded.Add((_target, _bot.World.Now + SpyGadgetK.BlindSeconds));
                }

                break;
            case SpyGadgetAct.StickyCam:
                CamsPlaced++;
                break;
        }
    }

    // ---- aiming ---------------------------------------------------------------------------------------------------

    private void LookAt(FVector point, ref AiPlan plan)
    {
        FRotator r = (point - AiSight.Eye(_bot.Pawn!)).Rotation();
        plan.Yaw = r.Yaw & 0xFFFF;
        plan.Pitch = r.Pitch;
        plan.Tolerance = 65536;
    }

    private static int Wrap(int angle) => ((angle + 32768) & 0xFFFF) - 32768;

    /// <summary>
    /// The aim of a full-strength throw at the target point (the target pawn followed while seen, led by its speed):
    /// the low arc for the throw's speed under the grenade's gravity, plus the difficulty's aim spread.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "grenade-throw.md rules 2 to 4a: a tap throws at v = 1, Force = (v + 1)^2 x 900 / 2, launch Force + 210, falling at 750 uu/s^2")]
    private FRotator ThrowAim(SBasePawn spy)
    {
        FVector eye = AiSight.Eye(spy);
        FVector point = _aimPoint;
        if (_target is not null && _bot.Memory.Find(_target) is { Visible: true })
        {
            // Smoke and flash go at the body (they pop on touch); chaff at the feet, as planned.
            point = _act == SpyGadgetAct.Chaff ? _aimPoint : _target.Location;
            float t0 = (point - eye).Size() / SpyGadgetK.ThrowSpeed;
            point += _target.Velocity * (t0 * _bot.Difficulty.LeadFactor);
        }

        FVector to = point - eye;
        float x = SvmMath.Sqrt(to.X * to.X + to.Y * to.Y);
        float y = to.Z;
        float s = SpyGadgetK.ThrowSpeed;
        float g = SpyGadgetK.GrenadeGravity;
        float disc = s * s * s * s - g * (g * x * x + 2f * y * s * s);
        float angle = disc < 0f || x < 1f ? MathF.PI / 4f : SvmMath.Atan((s * s - SvmMath.Sqrt(disc)) / (g * x));
        int yaw = to.Rotation().Yaw & 0xFFFF;
        int pitch = (int)(angle * (32768f / MathF.PI));
        float spread = _bot.Difficulty.AimSettledDegrees * (65536f / 360f);
        return new FRotator(pitch + (int)(_bot.Random.RandRange(-spread, spread) * 0.5f), yaw + (int)_bot.Random.RandRange(-spread, spread), 0);
    }

    /// <summary>
    /// A wall the cam can stick on (the stick line's flags and surface test, sticky-cam.md rule 2) from where the
    /// gas point lands within reach of <paramref name="spot"/>: the cam turns to face the pawn at the entry (within
    /// its limits round the wall's normal), and the gas goes 192 uu along its view. A few lines round the direction
    /// to the spot are tried; the best fit wins.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "sticky-cam.md rules 2, 10 and 22: 2.5 uu pull-back, the cam faces the pawn, gas 192 uu ahead, limits pitch 13000 / yaw 14000")]
    private bool FindCamSpot(SBasePawn spy, FVector spot, out FRotator aim)
    {
        aim = default;
        FVector eye = AiSight.Eye(spy);
        FVector to = spot - eye;
        float dist = to.Size();
        if (dist < 1f)
        {
            return false;
        }

        FRotator baseRot = to.Rotation();
        float best = SpyGadgetK.CamFit;
        bool found = false;
        for (int p = -1; p <= 1; p++)
        {
            for (int y = -5; y <= 5; y++)
            {
                var r = new FRotator(baseRot.Pitch + p * SpyGadgetK.CamProbeStep, baseRot.Yaw + y * SpyGadgetK.CamProbeStep, 0);
                FVector d = r.Vector();
                FVector end = eye + d * (dist + SpyGadgetK.CamProbeBeyond);
                if (!_bot.World.Collision.LineCheck(out FCheckResult hit, spy, end, eye, default, SGadStickyCam.StickTraceFlags) ||
                    !SGadStickyCam.CanStickOn(hit) || (hit.Location - eye).Size() < 200f)
                {
                    continue;
                }

                FVector cam = hit.Location - d * 2.5f;
                FRotator wall = SGadStickyCam.FacingBack(hit.Normal, d).Rotation();
                FRotator face = (spy.Location - cam).Rotation();
                int relYaw = Math.Clamp(Wrap(face.Yaw - wall.Yaw), -14000, 14000);
                int relPitch = Math.Clamp(Wrap(face.Pitch - wall.Pitch), -13000, 13000);
                FVector gas = cam + new FRotator(wall.Pitch + relPitch, wall.Yaw + relYaw, 0).Vector() * EK.StickyGasAhead;
                float fit = (gas - spot).Size();
                if (fit < best && _bot.World.LineOpen(cam, spot))
                {
                    best = fit;
                    aim = r;
                    found = true;
                }
            }
        }

        return found;
    }
}

/// <summary>Constants of the spy bots' gadget play (play choices unless marked decoded).</summary>
public static class SpyGadgetK
{
    [Provenance(ProvenanceKind.Estimated, "Bot play choice: how often a free bot looks for a gadget to use")]
    public const float CheckSeconds = 0.25f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: below this (none of the presets) no gadgets at all")]
    public const float MinGadgetUse = 0.1f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: aimed throws, the heartbeat and the cam from Normal (0.6) up; Easy only quick launches")]
    public const float AimedGadgetUse = 0.3f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: cosine from a merc's view within which he faces the spy (about 40 degrees)")]
    public const float FacingCos = 0.75f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: flash range (the flash itself reaches 2500 uu, flash-bang.md)")]
    public const float FlashMin = 200f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float FlashMax = 1800f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: a facing merc this close makes a smoke at the feet worth it")]
    public const float SmokeDefendRange = 1100f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: chaff jams within 300 uu (chaff-grenade.md), so only a merc about to reach the spy")]
    public const float ChaffCloseRange = 550f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: speed (uu/s) toward the spy that counts as closing in")]
    public const float ClosingSpeed = 80f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: a smoke covers the hack within this distance of the terminal")]
    public const float CoverHackRange = 450f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float CoverHackMercRange = 1600f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: a believed merc this close to the target terminal guards it")]
    public const float GuardRange = 800f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float GuardMin = 300f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float GuardMax = 1500f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: seconds between two tries at a guard")]
    public const float GuardPause = 6f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float SmokeAtGuardMax = 1200f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: waits for the quick throw's notify (0.37 s into the clip) and the stand-up")]
    public const float QuickWaitSeconds = 1.5f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: aim error (degrees) at which a throw goes, plus up to 3 for careless bots")]
    public const float ThrowAimDegrees = 1.5f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float CamAimDegrees = 0.6f;

    /// <summary>Launch speed of a tapped throw: (1 + 1)^2 x 900 / 2 + 210.</summary>
    [Provenance(ProvenanceKind.Decoded, "grenade-throw.md rules 2 to 4: v = 1 under the 0.25 s tap window, Force = (v + 1)^2 x ProjectileSpeed / 2, Velocity = D (Force + 210)")]
    public const float ThrowSpeed = 2010f;

    [Provenance(ProvenanceKind.Decoded, "grenade-throw.md rule 4a: a grenade's vertical speed falls by 750 uu/s per second")]
    public const float GrenadeGravity = 750f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: about how long a flashed merc is blind (the after-image fades from 1 to 0 over 8 s)")]
    public const float BlindSeconds = 4f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: length of a heartbeat sweep (half again when sweeping all round)")]
    public const float ScanSeconds = 1.6f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: 60 degrees each side of the terminal")]
    public const int ScanHalfSweep = 10923;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: seconds between scans at GadgetUse 1 (twice that at 0)")]
    public const float ScanEvery = 8f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float ScanMinRange = 400f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float ScanMaxRange = 3500f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: a mark on the radar is good to a few percent of its distance")]
    public const float RadarBlur = 0.03f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: a radar mark is surer than a noise, less than a sighting")]
    public const float SensorConfidence = 0.8f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: how far the cam's gas point may land from the guard's spot (the gas reaches 128 uu)")]
    public const float CamFit = 90f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: about 6 degrees between the lines tried for a cam")]
    public const int CamProbeStep = 1100;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: walls up to this far behind the spot are tried")]
    public const float CamProbeBeyond = 700f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: seconds to select, draw, aim and enter the cam")]
    public const float CamSetupSeconds = 6f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: longest watch through a cam (half of it at GadgetUse 0)")]
    public const float CamWatchSeconds = 15f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: a merc believed this close to the spy's body ends the watch")]
    public const float CamLeaveRange = 600f;

    /// <summary>Gas reach from the gas point: the 128 uu radius plus a margin (the cloud catches for 6 s).</summary>
    [Provenance(ProvenanceKind.Decoded, "sticky-cam.md rule 23: 128 uu round the gas point; the 40 uu margin is a play choice")]
    public const float GasReach = 168f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: seconds of a seen merc's speed taken into account")]
    public const float GasLead = 0.3f;
}
