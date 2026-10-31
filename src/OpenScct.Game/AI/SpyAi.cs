using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.AI;

/// <summary>
/// A spy bot. Goals, scored every decision period:
/// <list type="bullet">
/// <item><b>Hack</b>: the terminal with the cheapest route (light and known mercs make a route dearer, by the
/// difficulty's stealth), not claimed by a teammate; it sneaks there crouched (silent: crouched steps play no sound)
/// or runs when no merc is about, uses the terminal (stand still, hold Use until the menu opens, release) and waits
/// while the terminal states hack.</item>
/// <item><b>Evade</b>: when a merc it sees faces it and it is lit, or it was hurt: run to the darkest nearby spot out
/// of the merc's line of sight, with a quick-launched smoke grenade at the feet when the merc is close.</item>
/// <item><b>Hide</b>: a merc in sight that does not face it while it stands in the dark: crouch, freeze and watch.</item>
/// <item><b>Shock</b>: a merc closing in, facing the spy at mid range, or guarding the target terminal: draw the
/// shocker (a tap of the fire-mode key), wait for the armed stance, aim, fire, holster.</item>
/// <item><b>Grab</b>: a merc alone, turned away and in reach (in the dark, guarding the target or in the way): creep
/// up behind him, press Use (the grab), then Use again (the neck break; Fire knocks out when there is no room).</item>
/// <item><b>Drop</b>: a merc standing under a drop of the spy's graph: walk to the edge and step off onto his head
/// (the drop attack knocks him down).</item>
/// </list>
/// Rolls (the roll key while running) dodge a merc that is shooting or watching, and speed up a run on a long straight
/// stretch with no merc about. A drawn shocker is holstered whenever no shock is under way.
/// Night vision goes on in the dark and off in the light, thermal when smoke is near.
/// </summary>
public sealed class SpyAi : AiBrain
{
    public const int GIdle = 0;
    public const int GHack = 1;
    public const int GEvade = 2;
    public const int GHide = 3;
    public const int GShock = 4;
    public const int GGrab = 5;
    public const int GDrop = 6;

    private static readonly Name NAME_s_MenuInteraction = new("s_MenuInteraction");
    private static readonly Name NAME_s_PlayerWalkingMario_T_0HTo2H = new("s_PlayerWalkingMario_T_0HTo2H");
    private static readonly Name NAME_s_PlayerWalkingMario_T_2HToOH = new("s_PlayerWalkingMario_T_2HToOH");
    private static readonly Name NAME_s_GrabbingPlayer = new("s_GrabbingPlayer");

    private enum ShockPhase : byte
    {
        None,
        Draw,
        Aim,
        Check,
    }

    private enum GrabPhase : byte
    {
        Approach,
        Press,
        Hold,
    }

    private enum DropPhase : byte
    {
        Walk,
        Step,
        Fall,
    }

    private enum HackPhase : byte
    {
        Walk,
        Settle,
        PressUse,
        Asked,
        Hacking,
    }

    private SComputerObjectiveTrigger? _target;
    private HackPhase _hack;
    private float _hackAt;
    private int _asks;
    private int _useYaw;
    private readonly Dictionary<SComputerObjectiveTrigger, float> _givenUp = new(ReferenceEqualityComparer.Instance);
    private FVector _hideSpot;
    private float _hideSpotAt = -10f;
    private Pawn? _threat;
    private float _visionAt = -100f;
    private float _exposure;

    // Shock.
    private ShockPhase _shockPhase;
    private float _shockAt;
    private float _shockStart;
    private int _shockTries;
    private int _shots;
    private float _shockCooldownUntil;
    private SBasePawn? _shockTarget;
    private SBasePawn? _shockCandidate;
    private bool _shockUrgent;
    private float _shockRollAt = -100f;
    private bool _shockRollOk;
    private SBasePawn? _shockedPawn;
    private float _shockedAt = -100f;
    private float _holsterTapAt = -100f;

    // Grab and drop (melee).
    private float _meleeRollAt = -100f;
    private bool _meleeRollOk;
    private SBasePawn? _grabTarget;
    private SBasePawn? _grabCandidate;
    private GrabPhase _grab;
    private float _grabAt;
    private float _grabStart;
    private int _grabPresses;
    private float _grabCooldownUntil;
    private SBasePawn? _dropTarget;
    private SBasePawn? _dropCandidate;
    private FVector _dropTop;
    private FVector _dropLand;
    private FVector _dropTopCand;
    private FVector _dropLandCand;
    private DropPhase _drop;
    private float _dropAt;
    private float _dropStart;
    private float _dropCooldownUntil;
    private float _dropSearchAt = -100f;
    private float _letGoAt = -100f;

    // Rolls.
    private float _rollAt = -100f;
    private float _rollLookAt = -100f;
    private int _rollTicks;

    private float _hideCooldownUntil;
    private int _failures;
    private float _lastFailAt = -10f;
    private FVector _wander;
    private float _wanderAt = -100f;

    public SpyAi(AiController bot)
        : base(bot)
    {
    }

    protected override bool Spy => true;

    /// <summary>Terminals this spy saw taken while it hacked them.</summary>
    public int Hacked { get; private set; }

    /// <summary>Use requests sent (Use held until the menu opened).</summary>
    public int Requests { get; private set; }

    /// <summary>Smokes the gadget layer threw (SpyGadgets.cs plays the smoke now).</summary>
    public int SmokesThrown => Bot.Gadgets.Smokes;

    public int ShocksFired { get; private set; }

    /// <summary>The terminal the spy is going for, if any (SpyGadgets reads it).</summary>
    public SComputerObjectiveTrigger? Target => _target;

    /// <summary>At the terminal: settling, asking or hacking (no gadget then).</summary>
    public bool AtTerminal => Goal == GHack && _hack != HackPhase.Walk;

    /// <summary>In a melee move (a grab or a drop under way): no gadget then.</summary>
    public bool InMelee => (Goal == GGrab && _grabTarget is not null) || (Goal == GDrop && _dropTarget is not null) || InGrabStates();

    /// <summary>Shots that left the merc sticky-shocked.</summary>
    public int ShockHits { get; private set; }

    /// <summary>Fire-mode taps that holstered a gun left out.</summary>
    public int HolsterTaps { get; private set; }

    /// <summary>Use presses at a merc's back, grabs that held, and neck breaks asked for.</summary>
    public int GrabPresses { get; private set; }

    public int Grabs { get; private set; }

    public int NeckBreaks { get; private set; }

    /// <summary>Steps off a ledge onto a merc, and the drop attacks that landed.</summary>
    public int DropTries { get; private set; }

    public int DropAttacks { get; private set; }

    public int Rolls { get; private set; }

    public override string Status => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"spy {GoalName(Goal)} {_hack} target {_target?.TriggerName ?? "-"} path {Follower.NextIndex}/{Follower.Path.Count} links {Follower.LinksDone}/{Follower.LinksFailed} hacked {Hacked} shock {ShockHits}/{ShocksFired} grab {NeckBreaks}/{Grabs}/{GrabPresses} drop {DropAttacks}/{DropTries} rolls {Rolls}");

    public static string GoalName(int goal) => goal switch
    {
        GHack => "hack",
        GEvade => "evade",
        GHide => "hide",
        GShock => "shock",
        GGrab => "grab",
        GDrop => "drop",
        _ => "idle",
    };

    public override void Reset()
    {
        base.Reset();
        _target = null;
        _hack = HackPhase.Walk;
        _shockPhase = ShockPhase.None;
        _shockTarget = null;
        _grabTarget = null;
        _dropTarget = null;
        _rollTicks = 0;
    }

    // ---- choice ----------------------------------------------------------------------------------------------

    protected override int Choose(float now)
    {
        // A hack under way is finished unless the spy is being shot.
        bool hurt = now - Bot.HurtAt < 1.5f;
        if (Goal == GHack && _hack == HackPhase.Hacking && !hurt)
        {
            return GHack;
        }

        // A shock, a grab or a drop under way is finished (each has its own time limit).
        if (Goal == GShock && _shockPhase != ShockPhase.None && now - _shockStart < SpyAiK.ShockGiveUpSeconds)
        {
            return GShock;
        }

        if (Goal == GGrab && _grabTarget is not null && (_grab != GrabPhase.Approach || InGrabStates()))
        {
            return GGrab;
        }

        if (InGrabStates())
        {
            return GGrab;
        }

        if (Goal == GDrop && _dropTarget is not null && _drop != DropPhase.Walk)
        {
            return GDrop;
        }

        float exposure = Exposure(now, out Pawn? threat, out float threatDistance, out bool threatFacing);
        _threat = threat;
        _exposure = exposure;
        float myLight = World.LightOn(Pawn, threat is null ? AiSight.Eye(Pawn) + Pawn.Rotation.Vector() * 300f : AiSight.Eye(threat));
        Span<AiOption> options = stackalloc AiOption[8];
        int n = 0;
        options[n++] = new AiOption(GIdle, 0.05f);
        if (NextTerminal(now) is not null)
        {
            // Right after a shock the guard is stunned: the moment to hack.
            options[n++] = new AiOption(GHack, 0.6f + (now - _shockedAt < SpyAiK.StunHackWindow ? 0.6f : 0f));
        }

        if (exposure > 0.25f || hurt)
        {
            options[n++] = new AiOption(GEvade, 0.4f + exposure + (hurt ? 0.8f : 0f));
        }

        // Melee: a grab from behind, or a drop onto a merc's head, when the difficulty's melee use rolls for it.
        if (now - _meleeRollAt > SpyAiK.MeleeRollSeconds)
        {
            _meleeRollAt = now;
            _meleeRollOk = Bot.Random.FRand() < Difficulty.MeleeUse;
        }

        if (_meleeRollOk && !hurt && Bot.Gadgets.Active == SpyGadgetAct.None)
        {
            float grab = GrabChoice(now, myLight);
            if (grab > 0f)
            {
                options[n++] = new AiOption(GGrab, grab);
            }

            float drop = DropChoice(now);
            if (drop > 0f)
            {
                options[n++] = new AiOption(GDrop, drop);
            }
        }

        // Freeze in the dark while a merc goes by, a few seconds at a time (then the plan goes on round it).
        if (threat is not null && !threatFacing && threatDistance < 1100f && myLight < 0.25f &&
            (Goal == GHide ? now - GoalSince < 5f : now > _hideCooldownUntil))
        {
            options[n++] = new AiOption(GHide, 0.75f);
        }

        // The shocker (it stuns, it does not kill): a merc closing in, one facing the spy at mid range, or one guarding
        // the terminal the spy wants.
        float shock = ShockChoice(now, exposure, hurt);
        if (shock > 0f)
        {
            options[n++] = new AiOption(GShock, shock);
        }

        int pick = Pick(options[..n]);
        if (pick == GShock && Goal != GShock)
        {
            StartShock(now);
        }

        if (pick == GGrab && Goal != GGrab)
        {
            _grabTarget = _grabCandidate;
            _grab = GrabPhase.Approach;
            _grabStart = now;
            _grabAt = now;
            _grabPresses = 0;
        }

        if (pick == GDrop && Goal != GDrop)
        {
            _dropTarget = _dropCandidate;
            _dropTop = _dropTopCand;
            _dropLand = _dropLandCand;
            _drop = DropPhase.Walk;
            _dropStart = now;
            _dropAt = now;
        }

        return pick;
    }

    /// <summary>
    /// The shock's score, or 0. A merc in sight with an open line, not shocked already, and the spy with the gun and the
    /// energy for a shot (15): within 550 uu and facing the spy (closing in), within 350 (in its face), facing the spy
    /// at mid range while the spy is exposed, or guarding the target terminal. Looked at with the difficulty's gadget
    /// use once a second or so.
    /// </summary>
    private float ShockChoice(float now, float exposure, bool hurt)
    {
        _shockCandidate = null;
        if (now < _shockCooldownUntil || Bot.Gadgets.Active != SpyGadgetAct.None || Pawn.MyWeapon is not SWeaponAttaque || Pawn is not SPawnAttaque { EnergyLevel: >= SpyAiK.ShotEnergy } ||
            Pawn.Physics != EPhysics.PHYS_Walking || Pawn.HandItem is not null)
        {
            return 0f;
        }

        if (now - _shockRollAt > SpyAiK.ShockRollSeconds)
        {
            _shockRollAt = now;
            _shockRollOk = Bot.Random.FRand() < 0.25f + Difficulty.GadgetUse * 0.75f;
        }

        if (!_shockRollOk)
        {
            return 0f;
        }

        FVector eye = AiSight.Eye(Pawn);
        float best = 0f;
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            if (!c.Visible || !c.Confirmed || c.Pawn is not SBasePawn m || !Shockable(m, now))
            {
                continue;
            }

            float d = (m.Location - Pawn.Location).Size();
            if (d > SpyAiK.ShockRange || !World.LineOpen(eye, AiSight.Eye(m)))
            {
                continue;
            }

            bool facing = Faces(m, Pawn.Location, 0.75f);
            float bonus = 0f;
            if (d < 550f && facing)
            {
                bonus = 0.6f + (hurt ? 0.5f : 0f);
            }
            else if (d < 350f)
            {
                bonus = 0.5f;
            }
            else if (facing && d < 1000f && exposure > 0.35f)
            {
                bonus = 0.3f + (hurt ? 0.3f : 0f);
            }

            if (_target is { } t && (m.Location - t.Location).Size() < 900f)
            {
                bonus = SvmMath.Max(bonus, facing ? 0.3f : 0.45f);
            }

            if (bonus > best)
            {
                best = bonus;
                _shockCandidate = m;
                _shockUrgent = facing && d < 700f;
            }
        }

        return _shockCandidate is null ? 0f : 0.45f + Difficulty.GadgetUse * 0.5f + best;
    }

    /// <summary>A merc the shocker can stun: alive, on its feet, not shocked now nor by this spy in the last seconds.</summary>
    private bool Shockable(SBasePawn m, float now) =>
        m is SPAWNDEFENSE && m.Health > 0 && !m.bDeleteMe && !m.bStickyShocking && !m.bLyingOnGround && !m.bSleeping && !FallingDown(m) &&
        !(ReferenceEquals(m, _shockedPawn) && now - _shockedAt < SpyAiK.ReshockSeconds);

    /// <summary>A pawn going down (knocked out, gassed): its fall shows on screen.</summary>
    private static bool FallingDown(Pawn m) =>
        m.Controller is SPlayerController pc && pc.GetStateName().ToString().StartsWith("s_FallDown", StringComparison.Ordinal);

    /// <summary>True when <paramref name="m"/> faces <paramref name="point"/> (cosine above <paramref name="cos"/>).</summary>
    private static bool Faces(Pawn m, FVector point, float cos)
    {
        FVector to = point - m.Location;
        to.Z = 0f;
        float len = to.Size();
        if (len < 1f)
        {
            return true;
        }

        FVector view = m.Rotation.Vector();
        view.Z = 0f;
        float vl = view.Size();
        return vl > 0.01f && (to | view) / (len * vl) > cos;
    }

    /// <summary>
    /// The grab's score, or 0: a merc in sight on the spy's floor within 1400 uu, turned away from the spy, on his
    /// feet, alone (no other merc known within 1200 uu of him), not shocked in the last 5.5 s (the server refuses a
    /// grab within 5 s of a shock), and worth it: the spy in the dark, the merc guarding the target or in the way.
    /// </summary>
    private float GrabChoice(float now, float myLight)
    {
        _grabCandidate = null;
        if (now < _grabCooldownUntil || Pawn.Physics != EPhysics.PHYS_Walking || Pawn.HandItem is not null ||
            GameReplicationInfoGrabOff())
        {
            return 0f;
        }

        float bestScore = 0f;
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            // Seen now, or seen for sure a moment ago (the spy looked away on its walk).
            bool known = now - c.PoseTime < SpyAiK.GrabMemorySeconds && c.Confidence > 0.8f;
            if (!known || c.Pose is not { OnFeet: true } pose || c.Pawn is not SPAWNDEFENSE m ||
                (ReferenceEquals(m, _shockedPawn) && now - _shockedAt < SpyAiK.GrabAfterShockSeconds))
            {
                continue;
            }

            FVector to = Pawn.Location - pose.Position;
            float d = NavFollower.Horizontal(to);
            if (d > SpyAiK.GrabNoticeRange || MathF.Abs(Feet.Z - pose.Feet.Z) > 60f)
            {
                continue;
            }

            // Turned away: the spy in the rear half of his view, well behind him when close.
            if (d > 1f && (to | pose.Rotation.Vector()) / d > (d < 500f ? -0.3f : 0f) || !Alone(m, pose.Position))
            {
                continue;
            }

            bool guarding = _target is { } t && (pose.Position - t.Location).Size() < 900f;
            bool inTheWay = d < 500f;
            bool dark = myLight < 0.3f;
            if (!guarding && !inTheWay && !dark)
            {
                continue;
            }

            float score = 0.7f + Difficulty.MeleeUse * 0.5f + (guarding ? 0.3f : 0f) + (inTheWay ? 0.2f : 0f) - d / 4000f;
            if (score > bestScore)
            {
                bestScore = score;
                _grabCandidate = m;
            }
        }

        return bestScore;
    }

    private bool GameReplicationInfoGrabOff() => Pc.GameReplicationInfo is { bAvlb_SPY_Grab: false };

    /// <summary>A merc walking on his feet (the grab and the drop want him there).</summary>
    private static bool OnFeet(SBasePawn m) =>
        m.Health > 0 && !m.bDeleteMe && !m.bStickyShocking && !m.bLyingOnGround && !m.bSleeping && !FallingDown(m) &&
        m.Physics == EPhysics.PHYS_Walking;

    /// <summary>No other merc the spy knows of within 1200 uu of <paramref name="m"/>.</summary>
    private bool Alone(Pawn m, FVector at)
    {
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            if (!ReferenceEquals(c.Pawn, m) && c.Confidence > 0.35f && (c.Position - at).Size() < 1200f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The drop's score, or 0: a merc in sight standing below the spy (120 to 650 uu down, within 1000 uu across) on the
    /// landing of a drop edge of the spy's graph near the spy, not facing it, not wearing the shocker's sticky skin (a
    /// drop on him blasts the spy). Searched at most twice a second.
    /// </summary>
    private float DropChoice(float now)
    {
        _dropCandidate = null;
        if (now < _dropCooldownUntil || Pawn.Physics != EPhysics.PHYS_Walking || now - _dropSearchAt < 0.5f)
        {
            return 0f;
        }

        _dropSearchAt = now;
        FVector feet = Feet;
        float bestCost = float.MaxValue;
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            if (!c.Visible || !c.Confirmed || c.Pawn is not SPAWNDEFENSE m || !OnFeet(m) || m.bStickySkinned ||
                m.Velocity.Size() > 200f || Faces(m, Pawn.Location, 0.5f))
            {
                continue;
            }

            FVector mf = NavFollower.Feet(m);
            float below = feet.Z - mf.Z;
            if (below < SpyAiK.DropMinHeight || below > SpyAiK.DropMaxHeight || NavFollower.Horizontal(mf - feet) > 1000f)
            {
                continue;
            }

            if (FindDropOnto(mf, out FVector top, out FVector land, out float cost) && cost < bestCost)
            {
                bestCost = cost;
                _dropCandidate = m;
                _dropTopCand = top;
                _dropLandCand = land;
            }
        }

        return _dropCandidate is null ? 0f : 0.8f + Difficulty.MeleeUse * 0.5f - bestCost / 3000f;
    }

    /// <summary>A drop edge near the spy whose landing is within 70 uu of <paramref name="mercFeet"/>.</summary>
    private bool FindDropOnto(FVector mercFeet, out FVector top, out FVector land, out float cost)
    {
        top = default;
        land = default;
        cost = float.MaxValue;
        NavGraph g = Follower.Graph;
        List<int> nodes = NodesAround(Pawn.Location, 1000f);
        for (int i = 0; i < nodes.Count; i++)
        {
            int n = nodes[i];
            foreach ((int to, NavEdgeKind kind, _, _) in g.EdgesOf(n))
            {
                if (kind != NavEdgeKind.Drop)
                {
                    continue;
                }

                FVector l = g.Node(to).Floor;
                if (NavFollower.Horizontal(l - mercFeet) > SpyAiK.DropLandingReach || MathF.Abs(l.Z - mercFeet.Z) > 40f)
                {
                    continue;
                }

                FVector f = g.Node(n).Floor;
                float c = (f - Pawn.Location).Size();
                if (c < cost)
                {
                    cost = c;
                    top = f;
                    land = l;
                }
            }
        }

        return cost < float.MaxValue;
    }

    /// <summary>
    /// How exposed the spy is: a merc it sees (confirmed) that faces it, by distance, with the light on the spy as
    /// that merc sees it. Every input is what the spy's own screen shows (the merc's body and torch).
    /// </summary>
    private float Exposure(float now, out Pawn? threat, out float distance, out bool facing)
    {
        threat = null;
        distance = float.MaxValue;
        facing = false;
        float best = 0f;
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            // A merc frozen by a shock (its sparks show) cannot shoot.
            if (!c.Visible || !c.Confirmed || c.Pawn is SBasePawn { bStickyShocking: true })
            {
                continue;
            }

            FVector to = Pawn.Location - c.Pawn.Location;
            float d = to.Size();
            float cos = d > 1f ? (to | c.Pawn.Rotation.Vector()) / d : 1f;
            bool faces = cos > 0.75f;
            float light = World.LightOn(Pawn, AiSight.Eye(c.Pawn));
            float e = (faces ? 1f : 0.25f) * (light > 0.15f ? 1f : 0.35f) * SvmMath.Max(0f, 1f - d / 2500f);
            if (e > best || (threat is null))
            {
                if (e >= best)
                {
                    best = e;
                }

                if (d < distance)
                {
                    threat = c.Pawn;
                    distance = d;
                    facing = faces;
                }
            }
        }

        return best;
    }

    protected override void LeaveGoal(int from, int to, float now)
    {
        if (from == GHack && _hack is not HackPhase.Hacking)
        {
            _hack = HackPhase.Walk;
        }

        // A drawn gun left by a shock is holstered by Act's holster check.
        if (from == GShock && _shockPhase != ShockPhase.None)
        {
            EndShock(now, SpyAiK.ShockAbortCooldown);
        }

        if (from == GGrab)
        {
            _grabTarget = null;
            _grabCooldownUntil = SvmMath.Max(_grabCooldownUntil, now + 3f);
        }

        if (from == GDrop)
        {
            _dropTarget = null;
            _dropCooldownUntil = SvmMath.Max(_dropCooldownUntil, now + 3f);
        }

        if (from == GHide)
        {
            _hideCooldownUntil = now + 8f;
        }

        if (to != GHack)
        {
            World.Teams[0].Release(Bot);
        }

        Follower.Clear();
    }

    // ---- acting ----------------------------------------------------------------------------------------------

    protected override void Act(float now, ref AiPlan plan)
    {
        Vision(now, ref plan);
        if (LetGoOntoMerc(now, ref plan))
        {
            return;
        }

        switch (Goal)
        {
            case GHack:
                Hack(now, ref plan);
                break;
            case GEvade:
                Evade(now, ref plan);
                break;
            case GHide:
                Hide(ref plan);
                break;
            case GShock:
                Shock(now, ref plan);
                break;
            case GGrab:
                Grab(now, ref plan);
                break;
            case GDrop:
                Drop(now, ref plan);
                break;
            default:
                Wander(now, ref plan);
                break;
        }

        Roll(now, ref plan);
        Holster(now, ref plan);
    }

    /// <summary>
    /// A gun out with no shock under way (a goal changed mid-shock, a team goal took over, a roll with the fire-mode key)
    /// is holstered: one tick of the fire-mode key in the armed stance (a fresh press holsters, spy-shocker.md rule 5),
    /// then at least a second before the next try, so the key is seen released in between.
    /// </summary>
    private void Holster(float now, ref AiPlan plan)
    {
        // A gadget sequence (heartbeat sweep, aimed throw, cam) owns the fire-mode key and the drawn gun.
        if (Pawn.WeaponLoaded is not (1 or 2 or 7) || Pawn.bWeaponTransit || (Goal == GShock && _shockPhase != ShockPhase.None) ||
            Bot.Gadgets.Active != SpyGadgetAct.None ||
            Pc.IsInState(NAME_s_PlayerWalkingMario_T_0HTo2H) || Pc.IsInState(NAME_s_PlayerWalkingMario_T_2HToOH) ||
            now - _holsterTapAt < SpyAiK.HolsterRetrySeconds)
        {
            return;
        }

        _holsterTapAt = now;
        HolsterTaps++;
        plan.Keys.bFireMode = 1;
    }

    /// <summary>
    /// Hanging from a ledge right above a merc on his feet (not wearing sticky skin): let go onto his head (back on the
    /// stick, which OnLedge reads), when the melee roll allows it.
    /// </summary>
    private bool LetGoOntoMerc(float now, ref AiPlan plan)
    {
        if (Pawn.Physics != EPhysics.PHYS_Ledge || !_meleeRollOk)
        {
            return false;
        }

        foreach (AiContact c in Bot.Memory.Contacts)
        {
            if (c.Pawn is not SPAWNDEFENSE || c.Pose is not { OnFeet: true, StickySkinned: false } pose || now - c.PoseTime > 1f)
            {
                continue;
            }

            float below = Pawn.Location.Z - pose.Position.Z;
            if (NavFollower.Horizontal(pose.Position - Pawn.Location) < SpyAiK.LetGoReach && below > 120f && below < SpyAiK.DropMaxHeight + 200f)
            {
                plan.Keys = default;
                plan.Keys.BT_Down = 1;
                plan.Yaw = null;
                if (now - _letGoAt > 2f)
                {
                    DropTries++;
                }

                _letGoAt = now;
                return true;
            }
        }

        return false;
    }

    /// <summary>Nothing to do (every terminal given up for now): walk to a nearby spot, so a stuck spy moves on.</summary>
    private void Wander(float now, ref AiPlan plan)
    {
        plan.Crouched = true;
        if (now - _wanderAt > 6f || (Feet - _wander).Size() < 40f)
        {
            _wanderAt = now;
            List<int> nodes = NodesAround(Pawn.Location, 700f);
            if (nodes.Count == 0)
            {
                Follower.StartWiggle(now);
                return;
            }

            _wander = Follower.Graph.Node(nodes[Bot.Random.Rand(nodes.Count)]).Floor;
            Follower.Clear();
        }

        if (!MoveTo(_wander, now, ref plan, sneak: true) && now - _lastFailAt > 1.2f)
        {
            _lastFailAt = now;
            Follower.StartWiggle(now);
        }
    }

    private bool MercsAbout(float now)
    {
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            if (c.Confidence > 0.25f && (c.Position - Pawn.Location).Size() < 2200f)
            {
                return true;
            }
        }

        return false;
    }

    private SComputerObjectiveTrigger? NextTerminal(float now)
    {
        if (_target is { } current && !World.IsTaken(current) && !_givenUp.ContainsKey(current))
        {
            return current;
        }

        SComputerObjectiveTrigger? best = null;
        float bestScore = float.MaxValue;
        foreach (SComputerObjectiveTrigger t in World.Terminals)
        {
            if (World.IsTaken(t) || t.bTriggering && !ReferenceEquals(t, _target) || (_givenUp.TryGetValue(t, out float at) && now - at < 25f))
            {
                continue;
            }

            // Straight-line distance plus the known danger round it; teammates' claims push it back.
            float score = (t.Location - Pawn.Location).Size();
            foreach (AiContact c in Bot.Memory.Contacts)
            {
                if (c.Confidence > 0.2f)
                {
                    float d = (c.Position - t.Location).Size();
                    if (d < 1000f)
                    {
                        score += (1000f - d) * c.Confidence * Difficulty.Stealth * 2f;
                    }
                }
            }

            if (World.Teams[0].IsClaimedByOther(t, Bot))
            {
                score += 4000f * (0.3f + Difficulty.Teamwork);
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return best;
    }

    private void Hack(float now, ref AiPlan plan)
    {
        switch (_hack)
        {
            case HackPhase.Walk:
            {
                SComputerObjectiveTrigger? t = NextTerminal(now);
                if (t is null)
                {
                    plan.Crouched = true;
                    return;
                }

                if (!ReferenceEquals(t, _target))
                {
                    _target = t;
                    _asks = 0;
                    Follower.Clear();
                    World.Teams[0].Claim(t, Bot);
                }

                if (!UseSpot(t, out FVector floor, out _useYaw))
                {
                    GiveUp(now);
                    return;
                }

                bool sneak = Difficulty.Stealth >= 0.5f || MercsAbout(now);
                if (!MoveTo(floor, now, ref plan, sneak, Difficulty.Stealth, DangerPenalty(Difficulty.Stealth)))
                {
                    // No way from here: wiggle free a couple of times before the terminal is given up.
                    if (Follower.Failed && now - _lastFailAt > 1.2f)
                    {
                        _lastFailAt = now;
                        if (++_failures >= 3)
                        {
                            _failures = 0;
                            GiveUp(now);
                        }
                        else
                        {
                            Follower.StartWiggle(now);
                        }
                    }

                    return;
                }

                _failures = 0;

                if (Follower.Arrived)
                {
                    EnterHack(HackPhase.Settle, now);
                    plan.Yaw = _useYaw;
                }

                return;
            }

            case HackPhase.Settle:
                plan.Yaw = _useYaw;
                plan.Crouched = null;

                // The gun out (the holster check puts it away): the Use key would not reach the terminal.
                if (now - _hackAt < 0.4f || Pawn.WeaponLoaded != 0 || !Pawn.Velocity.IsZero() || NavFollower.YawError(Pc.Rotation.Yaw & 0xFFFF, _useYaw) > 600)
                {
                    if (now - _hackAt > 3f)
                    {
                        EnterHack(HackPhase.Walk, now);
                        Follower.Clear();
                    }

                    return;
                }

                _asks++;
                Requests++;
                EnterHack(HackPhase.PressUse, now);
                plan.Keys.bUsing = 1;
                return;

            case HackPhase.PressUse:
                plan.Yaw = _useYaw;
                if (Pc.IsInState(NAME_s_MenuInteraction) || now - _hackAt > 1f)
                {
                    EnterHack(HackPhase.Asked, now);
                    return;
                }

                plan.Keys.bUsing = 1;
                return;

            case HackPhase.Asked:
                plan.Yaw = _useYaw;
                if (_target is null || World.IsTaken(_target))
                {
                    EnterHack(HackPhase.Walk, now);
                    return;
                }

                if (_target.bTriggering || InTerminalStates())
                {
                    EnterHack(HackPhase.Hacking, now);
                }
                else if (now - _hackAt > 3f)
                {
                    if (_asks >= 3)
                    {
                        GiveUp(now);
                    }
                    else
                    {
                        // Back to the spot (the menu or a bump may have moved the spy) and ask again.
                        EnterHack(HackPhase.Walk, now);
                        Follower.Clear();
                    }
                }

                return;

            case HackPhase.Hacking:
                // The terminal states own the pawn while they walk it to the spot and hack: no input.
                plan.Yaw = null;
                plan.Crouched = null;
                if (_target is null)
                {
                    EnterHack(HackPhase.Walk, now);
                    return;
                }

                if (World.IsTaken(_target))
                {
                    Hacked++;
                    World.Teams[0].Release(Bot);
                    _target = null;
                    EnterHack(HackPhase.Walk, now);
                    return;
                }

                if (!_target.bTriggering && !InTerminalStates() && now - _hackAt > 1f)
                {
                    EnterHack(HackPhase.Walk, now);
                    Follower.Clear();
                }

                // The walk to the use spot that never arrives (a terminal collider that keeps the pawn short of the
                // spot, docs/AI.md): the terminal is left for later.
                if (Pc.IsInState(NAME_s_Computer_GoTo) && now - _hackAt > 8f)
                {
                    GiveUp(now);
                }

                return;
        }
    }

    private static readonly Name NAME_s_Computer_GoTo = new("s_Computer_GoTo");

    private void EnterHack(HackPhase phase, float now)
    {
        _hack = phase;
        _hackAt = now;
    }

    private void GiveUp(float now)
    {
        if (_target is not null)
        {
            _givenUp[_target] = now;
        }

        World.Teams[0].Release(Bot);
        _target = null;
        Follower.Clear();
        EnterHack(HackPhase.Walk, now);
    }

    private bool InTerminalStates() => Pc.GetStateName().ToString().StartsWith("s_Computer", StringComparison.Ordinal);

    private void Evade(float now, ref AiPlan plan)
    {
        Pawn? threat = _threat;
        FVector threatEye = (threat is not null ? Bot.Memory.Find(threat)?.BelievedEye : null) ??
            Bot.Memory.Best()?.BelievedEye ?? Pawn.Location;

        if (now - _hideSpotAt > 1.5f || (Pawn.Location - _hideSpot).Size() < 40f)
        {
            _hideSpot = FindHideSpot(threatEye);
            _hideSpotAt = now;
        }

        if (!MoveTo(_hideSpot, now, ref plan, sneak: false, stealth: Difficulty.Stealth * 0.5f, DangerPenalty(1f)))
        {
            // Nowhere to go: at least get low.
            plan.Crouched = true;
        }
        else
        {
            plan.Crouched = false;
        }
    }

    /// <summary>The best spot within 900 uu: out of the threat's line of sight, dark, and away from it.</summary>
    private FVector FindHideSpot(FVector threatEye)
    {
        List<int> nodes = NodesAround(Pawn.Location, 900f);
        NavGraph g = Follower.Graph;
        FVector best = Feet;
        float bestScore = float.MaxValue;
        int stride = Math.Max(1, nodes.Count / 48);
        for (int i = 0; i < nodes.Count; i += stride)
        {
            FVector floor = g.Node(nodes[i]).Floor;
            float light = g.HasLight ? SvmMath.Max(0f, g.LightOf(nodes[i])) : 0.3f;
            bool seen = World.LineOpen(threatEye, floor + new FVector(0f, 0f, 60f));
            float away = (floor - threatEye).Size();
            float score = (seen ? 3f : 0f) + light * 2f - away / 1500f + (floor - Pawn.Location).Size() / 1200f;
            if (score < bestScore)
            {
                bestScore = score;
                best = floor;
            }
        }

        return best;
    }

    private void Hide(ref AiPlan plan)
    {
        plan.Crouched = true;
        if (_threat is not null && Bot.Memory.Find(_threat) is { } contact)
        {
            LookAt(contact.Position, ref plan);
            plan.Tolerance = 65536;
        }
    }

    private void StartShock(float now)
    {
        _shockTarget = _shockCandidate;
        _shockPhase = ShockPhase.Draw;
        _shockStart = now;
        _shockAt = now;
        _shockTries = 0;
        _shots = 0;

        // A merc closing in: the quick draw (a tap), not the evasion's silent long press.
        if (_shockUrgent)
        {
            Bot.Evasion.QuickDrawUntil = now + SpyAiK.DrawWaitSeconds;
        }
    }

    private void EndShock(float now, float cooldown)
    {
        _shockPhase = ShockPhase.None;
        _shockTarget = null;
        _shockCooldownUntil = now + cooldown;
    }

    /// <summary>Drawn, armed and out of the draw: a Fire press reaches the pistol (spy-shocker.md rules 7 and 8).</summary>
    private bool ReadyToFire() =>
        Pawn.WeaponLoaded == 2 && !Pawn.bWeaponTransit && !Pc.IsInState(NAME_s_PlayerWalkingMario_T_0HTo2H) &&
        !Pc.IsInState(NAME_s_PlayerWalkingMario_T_2HToOH);

    /// <summary>
    /// The shock, through the keys: a tap of the fire-mode key draws (released inside 0.5 s: with the laser; the
    /// evasion may hold it past 0.5 s for the silent draw); Fire only once the armed stance is reached (the draw
    /// transition ignores Fire and the pistol refuses a shot in transit); one more shot on a miss; the holster check
    /// puts the gun away after.
    /// </summary>
    private void Shock(float now, ref AiPlan plan)
    {
        SBasePawn? target = _shockTarget;
        if (_shockPhase == ShockPhase.None)
        {
            // Done: keep still a moment while the holster check puts the gun away.
            plan.Crouched = null;
            return;
        }

        AiContact? contact = target is not null ? Bot.Memory.Find(target) : null;
        if (target is null || contact?.Pose is not { Alive: true } || now - _shockStart > SpyAiK.ShockGiveUpSeconds)
        {
            EndShock(now, SpyAiK.ShockAbortCooldown);
            return;
        }

        plan.Crouched = null;
        FVector aimPoint = contact.Position + new FVector(0f, 0f, SpyAiK.AimChestUp);
        LookAt(aimPoint, ref plan);
        plan.Tolerance = 65536;
        switch (_shockPhase)
        {
            case ShockPhase.Draw:
                if (ReadyToFire())
                {
                    _shockPhase = ShockPhase.Aim;
                    _shockAt = now;
                    return;
                }

                if (Pawn.WeaponLoaded != 0 || Pc.IsInState(NAME_s_PlayerWalkingMario_T_0HTo2H))
                {
                    // Drawing.
                    return;
                }

                if (now - _shockAt < SpyAiK.TapSeconds)
                {
                    plan.Keys.bFireMode = 1;
                    return;
                }

                if (now - _shockAt > SpyAiK.DrawWaitSeconds)
                {
                    // No draw (another state took the press): once more, then give up.
                    if (++_shockTries >= 2)
                    {
                        EndShock(now, SpyAiK.ShockAbortCooldown);
                        return;
                    }

                    _shockAt = now;
                }

                return;

            case ShockPhase.Aim:
                if (!contact.BodyVisible)
                {
                    EndShock(now, SpyAiK.ShockAbortCooldown);
                    return;
                }

                if (!ReadyToFire())
                {
                    if (Pawn.WeaponLoaded == 0)
                    {
                        _shockPhase = ShockPhase.Draw;
                        _shockAt = now;
                    }

                    return;
                }

                if (now - _shockAt < SvmMath.Min(Difficulty.ReactionTime, SpyAiK.MaxAimSettle))
                {
                    return;
                }

                if (AimErrorTo(aimPoint) < 2f + Difficulty.AimSettledDegrees || now - _shockAt > SpyAiK.MaxAimSeconds)
                {
                    plan.Keys.Press(AiCommand.Fire);
                    plan.Keys.bFire = 1;
                    ShocksFired++;
                    _shots++;
                    _shockPhase = ShockPhase.Check;
                    _shockAt = now;
                }

                return;

            default:
                if (contact.BodyVisible && target.bStickyShocking)
                {
                    ShockHits++;
                    _shockedPawn = target;
                    _shockedAt = now;
                    EndShock(now, SpyAiK.ShockCooldown);
                    return;
                }

                if (now - _shockAt > SpyAiK.HitWaitSeconds)
                {
                    if (_shots < 2 && Pawn is SPawnAttaque { EnergyLevel: >= SpyAiK.ShotEnergy })
                    {
                        _shockPhase = ShockPhase.Aim;
                        _shockAt = now - SpyAiK.MaxAimSettle;
                    }
                    else
                    {
                        EndShock(now, SpyAiK.ShockMissCooldown);
                    }
                }

                return;
        }
    }

    // ---- grab ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The grab (spy-grab.md rules 1 to 4 and 13): creep to a spot behind the merc on the side the grab test favours
    /// (his yaw + 7200), walk the last steps straight at him so the spy faces him, press Use when the controller's own
    /// test passes (within 160 uu, he in front within 41 degrees, his shifted facing within 60 degrees of the spy's);
    /// held, release Use and press it again (the neck break); after a few presses with no room for it, Fire (the
    /// knock-out). The 15 s hold chokes him anyway.
    /// </summary>
    private void Grab(float now, ref AiPlan plan)
    {
        SBasePawn? m = _grabTarget;
        if (_grab == GrabPhase.Hold || InGrabStates())
        {
            GrabHold(now, ref plan);
            return;
        }

        AiContact? contact = m is not null ? Bot.Memory.Find(m) : null;
        if (m is null || contact?.Pose is not { OnFeet: true } pose || now - contact.PoseTime > SpyAiK.GrabMemorySeconds ||
            now - _grabStart > SpyAiK.GrabApproachSeconds)
        {
            EndGrab(now, SpyAiK.GrabFailCooldown);
            return;
        }

        FVector toSpy = Pawn.Location - pose.Position;
        float d = NavFollower.Horizontal(toSpy);

        // He turned round: no grab (the shocker or the evasion takes over).
        if (d > 1f && d < 800f && (toSpy | pose.Rotation.Vector()) / d > 0.3f)
        {
            EndGrab(now, SpyAiK.GrabFailCooldown);
            return;
        }

        if (_grab == GrabPhase.Press)
        {
            plan.Crouched = null;
            plan.Yaw = NavFollower.YawTo(Pawn.Location, pose.Position);
            plan.Tolerance = 65536;
            if (now - _grabAt < SpyAiK.UsePressSeconds)
            {
                plan.Keys.bUsing = 1;
                return;
            }

            if (now - _grabAt > SpyAiK.GrabAnswerSeconds)
            {
                _grab = GrabPhase.Approach;
                _grabAt = now;
            }

            return;
        }

        FVector shifted = new FRotator(0, (pose.Rotation.Yaw + SpyAiK.GrabAreaShift) & 0xFFFF, 0).Vector();
        shifted.Z = 0f;
        FVector toMerc = pose.Position - Pawn.Location;
        toMerc.Z = 0f;
        bool lined = d > 1f && (shifted | toMerc) / d > SpyAiK.GrabLineCos;
        if (d > SpyAiK.GrabFinalReach || !lined)
        {
            FVector behind = pose.Position - shifted.SafeNormal() * SpyAiK.GrabBehind;
            FVector floor = new(behind.X, behind.Y, pose.Feet.Z);
            if (!MoveTo(floor, now, ref plan, sneak: true, Difficulty.Stealth, DangerPenalty(Difficulty.Stealth * 0.5f)) && Follower.Failed)
            {
                EndGrab(now, SpyAiK.GrabFailCooldown);
            }

            return;
        }

        // The last steps: straight at him, standing at the walk (silent: walking steps play no sound), so the pawn
        // turns to face him. Standing, as the grab from a crouch first has to stand the spy up.
        plan.Crouched = false;
        plan.Yaw = NavFollower.YawTo(Pawn.Location, pose.Position);
        plan.Tolerance = 3000;
        plan.Keys = default;
        plan.Keys.BT_Up = (byte)(d > SpyAiK.GrabStopDistance ? 1 : 0);
        plan.Keys.FastSpeed = false;
        if (!Pawn.bIsCrouched && contact.BodyVisible && Pc.CanGrabPawn(m) && now - _grabAt > SpyAiK.GrabAnswerSeconds)
        {
            _grab = GrabPhase.Press;
            _grabAt = now;
            _grabPresses++;
            GrabPresses++;
            plan.Keys.bUsing = 1;
            if (_grabPresses > 3)
            {
                EndGrab(now, SpyAiK.GrabFailCooldown);
            }
        }
    }

    private void GrabHold(float now, ref AiPlan plan)
    {
        plan.Crouched = null;
        plan.Yaw = null;
        if (_grab != GrabPhase.Hold)
        {
            _grab = GrabPhase.Hold;
            _grabAt = now;
            _grabPresses = 0;
            Grabs++;
        }

        if (!InGrabStates())
        {
            EndGrab(now, SpyAiK.GrabDoneCooldown);
            return;
        }

        if (!Pc.IsInState(NAME_s_GrabbingPlayer))
        {
            // Getting into the hold (or letting go): Use released so the next press is a fresh one.
            _grabAt = now;
            return;
        }

        float since = now - _grabAt;
        if (since < SpyAiK.GrabHoldWait)
        {
            return;
        }

        if (since < SpyAiK.GrabHoldWait + SpyAiK.UsePressSeconds)
        {
            if (_grabPresses < SpyAiK.NeckBreakPresses)
            {
                plan.Keys.bUsing = 1;
            }
            else if (since - SpyAiK.GrabHoldWait < Sim.DeltaTime * 1.5f)
            {
                // No room to drop him for the neck break: knock him out (Fire).
                plan.Keys.Press(AiCommand.Fire);
            }

            return;
        }

        _grabPresses++;
        if (_grabPresses <= SpyAiK.NeckBreakPresses)
        {
            NeckBreaks++;
        }

        _grabAt = now;
    }

    private void EndGrab(float now, float cooldown)
    {
        _grabTarget = null;
        _grab = GrabPhase.Approach;
        _grabCooldownUntil = now + cooldown;
        Follower.Clear();
    }

    /// <summary>The spy's grab states, from the request to the release.</summary>
    private bool InGrabStates()
    {
        string s = Pc.GetStateName().ToString();
        return s.StartsWith("s_Grabbing", StringComparison.Ordinal) || s.StartsWith("s_Grab_", StringComparison.Ordinal) ||
            s.StartsWith("s_ReleaseGrabbedPlayer", StringComparison.Ordinal);
    }

    // ---- drop attack -----------------------------------------------------------------------------------------

    private static readonly Name NAME_s_DropAttack_Landing = new("s_DropAttack_Landing");

    /// <summary>
    /// The drop attack (drop-attack.md rules 1 and 3): walk to the top of the drop edge whose landing is under the
    /// merc, stand, walk off toward him (not run, as the follower's drops), steer at him while falling; landing on
    /// him knocks him down.
    /// </summary>
    private void Drop(float now, ref AiPlan plan)
    {
        SBasePawn? m = _dropTarget;
        AiContact? contact = m is not null ? Bot.Memory.Find(m) : null;
        if (m is null || contact?.Pose is not { Alive: true } pose || now - _dropStart > SpyAiK.DropGiveUpSeconds)
        {
            EndDrop(now);
            return;
        }

        if (Pc.IsInState(NAME_s_DropAttack_Landing))
        {
            DropAttacks++;
            EndDrop(now);
            return;
        }

        switch (_drop)
        {
            case DropPhase.Walk:
                // He walked off the landing, or would blast the spy: no drop.
                if (!pose.OnFeet || pose.StickySkinned || now - contact.PoseTime > 1f ||
                    NavFollower.Horizontal(pose.Feet - _dropLand) > SpyAiK.DropLandingReach + 40f)
                {
                    EndDrop(now);
                    return;
                }

                if (NavFollower.Horizontal(Feet - _dropTop) < 28f && MathF.Abs(Feet.Z - _dropTop.Z) < 40f)
                {
                    _drop = DropPhase.Step;
                    _dropAt = now;
                    DropTries++;
                    return;
                }

                if (!MoveTo(_dropTop, now, ref plan, sneak: true, Difficulty.Stealth) && Follower.Failed)
                {
                    EndDrop(now);
                }

                return;

            case DropPhase.Step:
                // Standing (a slow crouched walk off an edge catches the ledge), walking at him.
                plan.Crouched = false;
                plan.Yaw = NavFollower.YawTo(Pawn.Location, contact.Position);
                plan.Tolerance = 2500;
                plan.Keys = default;
                plan.Keys.BT_Up = (byte)(Pawn.bIsCrouched ? 0 : 1);
                plan.Keys.FastSpeed = false;
                if (Pawn.Physics == EPhysics.PHYS_Falling)
                {
                    _drop = DropPhase.Fall;
                    _dropAt = now;
                }
                else if (now - _dropAt > SpyAiK.DropStepSeconds)
                {
                    EndDrop(now);
                }

                return;

            default:
                plan.Crouched = null;
                if (Pawn.Physics == EPhysics.PHYS_Ledge)
                {
                    // Caught the edge: let go.
                    plan.Keys = default;
                    plan.Keys.BT_Down = 1;
                    return;
                }

                if (Pawn.Physics != EPhysics.PHYS_Falling)
                {
                    if (now - _dropAt > 0.3f)
                    {
                        EndDrop(now);
                    }

                    return;
                }

                // Air steering toward his head.
                plan.Yaw = NavFollower.YawTo(Pawn.Location, contact.Position);
                plan.Tolerance = 4000;
                plan.Keys = default;
                plan.Keys.BT_Up = (byte)(NavFollower.Horizontal(contact.Position - Pawn.Location) > 25f ? 1 : 0);
                return;
        }
    }

    private void EndDrop(float now)
    {
        _dropTarget = null;
        _drop = DropPhase.Walk;
        _dropCooldownUntil = now + SpyAiK.DropCooldown;
        Follower.Clear();
    }

    // ---- roll ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The roll key (roll.md: walking, unarmed, moving at 65 uu/s or more, 0.5 s between rolls; a standing run gives
    /// the dive at about 600 uu/s for a second, which the keys do not steer): evading, or running for a terminal
    /// while a merc sees the spy or it was just hurt, a roll is a dodge; running with no merc about, a faster stretch.
    /// Only along a straight, open stretch of the route at least as long as the roll, with the view and the body on it.
    /// </summary>
    private void Roll(float now, ref AiPlan plan)
    {
        if (_rollTicks > 0)
        {
            _rollTicks--;
            plan.Keys.bRoll = 1;
            return;
        }

        bool urgent;
        if (Goal == GEvade)
        {
            urgent = true;
        }
        else if (Goal == GHack && _hack == HackPhase.Walk)
        {
            urgent = _exposure > 0.35f || now - Bot.HurtAt < 3f;
            if (!urgent && (plan.Crouched == true || MercsAbout(now)))
            {
                return;
            }
        }
        else
        {
            return;
        }

        if (now - _rollAt < (urgent ? SpyAiK.RollDodgeCooldown : SpyAiK.RollRunCooldown) || now - _rollLookAt < 0.5f ||
            Pawn.Physics != EPhysics.PHYS_Walking || Pawn.WeaponLoaded != 0 || (Pawn.bIsCrouched && !urgent) ||
            Bot.Gadgets.Active != SpyGadgetAct.None ||
            plan.Keys.BT_Up == 0 || (plan.Keys.BT_Down | plan.Keys.BT_Left | plan.Keys.BT_Right) != 0 || plan.MoveYaw is not null ||
            plan.Yaw is not int yaw || NavFollower.Horizontal(Pawn.Velocity) < 200f ||
            NavFollower.YawError(Pc.Rotation.Yaw & 0xFFFF, yaw) > 1200 || NavFollower.YawError(Pawn.Rotation.Yaw & 0xFFFF, yaw) > 1800)
        {
            return;
        }

        _rollLookAt = now;
        if (StraightAhead() < (urgent ? SpyAiK.RollDodgeClear : SpyAiK.RollRunClear) ||
            Bot.Random.FRand() > (urgent ? 0.3f + 0.6f * Difficulty.Stealth : 0.15f + 0.5f * Difficulty.Stealth))
        {
            return;
        }

        _rollAt = now;
        _rollTicks = 2;
        plan.Keys.bRoll = 1;
        Rolls++;
    }

    /// <summary>
    /// The length of the route ahead that is one straight walk on one level (within about 11 degrees of the
    /// direction to the next waypoint, no drops, crawls or links), with a clear line along it.
    /// </summary>
    private float StraightAhead()
    {
        IReadOnlyList<NavStep> path = Follower.Path;
        int i = Follower.NextIndex;
        if (i >= path.Count || Follower.ActiveLink >= 0)
        {
            return 0f;
        }

        FVector feet = Feet;
        FVector dir = path[i].Point - feet;
        dir.Z = 0f;
        float first = dir.Size();
        if (first < 1f)
        {
            return 0f;
        }

        dir *= 1f / first;
        float best = 0f;
        FVector far = feet;
        for (; i < path.Count; i++)
        {
            NavStep s = path[i];
            if (s.Link >= 0 || s.Kind != NavEdgeKind.Walk)
            {
                break;
            }

            FVector d = s.Point - feet;
            if (MathF.Abs(d.Z) > 40f)
            {
                break;
            }

            d.Z = 0f;
            float l = d.Size();
            if (l > 1f && (d | dir) / l < 0.98f)
            {
                break;
            }

            best = l;
            far = s.Point;
        }

        return best > 0f && Follower.Clear(feet, far, crouched: false) ? best : 0f;
    }

    private void Vision(float now, ref AiPlan plan)
    {
        if (Difficulty.GadgetUse < 0.15f || now - _visionAt < 3f || Goal == GHack && _hack is HackPhase.PressUse or HackPhase.Asked or HackPhase.Hacking)
        {
            return;
        }

        int mode = AiSight.RendMapOf(Pc);
        bool smoke = World.InSmoke(Pawn.Location, 400f);
        float light = World.LightAround(Feet);
        int want = smoke ? SGoggle.REN_ThermalVision : light < 0.12f ? SGoggle.REN_NightVision : SGoggle.REN_DynLight;
        if (want == mode)
        {
            return;
        }

        _visionAt = now;
        if (want == SGoggle.REN_DynLight)
        {
            // The active mode's key turns it off.
            plan.Keys.Press(AiCommand.SwitchHeadset, mode == SGoggle.REN_ThermalVision ? 2 : 1);
        }
        else
        {
            plan.Keys.Press(AiCommand.SwitchHeadset, want == SGoggle.REN_ThermalVision ? 2 : 1);
        }
    }
}

/// <summary>Constants of the spy bots' shock, grab, drop and roll (play choices unless decoded).</summary>
public static class SpyAiK
{
    [Provenance(ProvenanceKind.Decoded, "spy-shocker.md: SStickyShocker EnergyNeeded 15")]
    public const float ShotEnergy = 15f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: a sure shot with the aim error of the presets")]
    public const float ShockRange = 1300f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: how often the gadget-use roll for a shock is made")]
    public const float ShockRollSeconds = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "spy-shocker-owner-notes.md / sticky-shock-effect.md: re-shock after 12.5 s")]
    public const float ReshockSeconds = 12.5f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: draw (up to 1 s), aim, two shots")]
    public const float ShockGiveUpSeconds = 4.5f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float ShockAbortCooldown = 3f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float ShockCooldown = 8f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float ShockMissCooldown = 5f;

    /// <summary>The draw tap: released well inside the 0.5 s hold (spy-shocker.md rule 1).</summary>
    [Provenance(ProvenanceKind.Estimated, "Bot play choice under the decoded 0.5 s hold; the capture's tap was 6 frames")]
    public const float TapSeconds = 0.08f;

    /// <summary>Wait for a draw to start: the evasion's long press (0.55 s) plus the transition (0.3 s).</summary>
    [Provenance(ProvenanceKind.Estimated, "spy-shocker.md rule 15: hold 0.5 s + 18 frames of transition, with a margin")]
    public const float DrawWaitSeconds = 1.1f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: the reaction time, at most this, before the first shot")]
    public const float MaxAimSettle = 0.4f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float MaxAimSeconds = 1.2f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: the shot is a trace, the shock shows at once")]
    public const float HitWaitSeconds = 0.35f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: the merc's chest above his Location")]
    public const float AimChestUp = 20f;

    /// <summary>The window after a shock in which the hack scores higher (the merc is frozen, then shaken).</summary>
    [Provenance(ProvenanceKind.Decoded, "spy-shocker-owner-notes.md: freeze 3.5 s, electric 4.5 s")]
    public const float StunHackWindow = 4.5f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: the key seen released between two holster taps")]
    public const float HolsterRetrySeconds = 1f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: how often the melee-use roll is made")]
    public const float MeleeRollSeconds = 2f;

    [Provenance(ProvenanceKind.Decoded, "spy-grab.md rule 5: TimeShockForGrab < now - 5.0 (plus a margin)")]
    public const float GrabAfterShockSeconds = 5.5f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float GrabNoticeRange = 1400f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: a merc seen for sure this long ago still counts for a grab")]
    public const float GrabMemorySeconds = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "spy-grab.md Data: iGrabAreaShift 7200")]
    public const int GrabAreaShift = 7200;

    /// <summary>The approach line within about 45 degrees of the merc's shifted facing before the last steps.</summary>
    [Provenance(ProvenanceKind.Estimated, "Bot play choice inside the decoded 0.5 facing test")]
    public const float GrabLineCos = 0.7f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float GrabFinalReach = 230f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: behind the merc, inside fGrabDistance 160 once walked in")]
    public const float GrabBehind = 110f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: inside fGrabDistance 160, outside the two radii")]
    public const float GrabStopDistance = 100f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: a short press of Use")]
    public const float UsePressSeconds = 0.1f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float GrabAnswerSeconds = 1f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float GrabApproachSeconds = 12f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float GrabFailCooldown = 6f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float GrabDoneCooldown = 10f;

    /// <summary>Use released this long in the hold before the neck-break press (the press edge).</summary>
    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float GrabHoldWait = 0.5f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: neck-break presses before the knock-out")]
    public const int NeckBreakPresses = 3;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: a drop worth taking")]
    public const float DropMinHeight = 120f;

    [Provenance(ProvenanceKind.Estimated, "Under the spy graph's 700 uu drops (docs/AI.md)")]
    public const float DropMaxHeight = 650f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: the landing under the merc's feet, within the two radii")]
    public const float DropLandingReach = 70f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float DropGiveUpSeconds = 12f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float DropStepSeconds = 3f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float DropCooldown = 6f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice: the merc's head under the hanging spy")]
    public const float LetGoReach = 45f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice over the decoded 0.5 s roll cooldown")]
    public const float RollDodgeCooldown = 1.5f;

    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float RollRunCooldown = 4f;

    /// <summary>The straight route a roll needs: the dive clears 350 uu and travels about 600 (roll.md).</summary>
    [Provenance(ProvenanceKind.Estimated, "roll.md: 350 uu clearance, 603 uu/s for about 1 s")]
    public const float RollDodgeClear = 450f;

    [Provenance(ProvenanceKind.Estimated, "roll.md: 350 uu clearance, 603 uu/s for about 1 s")]
    public const float RollRunClear = 650f;
}
