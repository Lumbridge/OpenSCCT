using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.AI;

/// <summary>Tuning of the merc bot's close fighting (choices of the bots, not game values; docs/AI.md).</summary>
public static class MercAiK
{
    /// <summary>A downed spy is finished from this far (path start distance, uu).</summary>
    public const float FinishRange = 1500f;

    /// <summary>A downed spy out of sight is still taken as down this long after he was last seen lying (a charge or spin KO lasts 3 s).</summary>
    public const float FinishMemorySeconds = 3f;

    /// <summary>The merc walks up to a downed spy until this close (horizontal), then turns to him and presses Use.</summary>
    public const float FinishWalkUntil = 140f;

    /// <summary>The Use press for the finishing move only this far inside the game's 250 uu search (the press acts a tick later).</summary>
    public const float ReachMargin = 25f;

    /// <summary>The finishing press wants the body facing the downed spy at least this much (the game takes over -0.5).</summary>
    public const float TerminateFacingDot = 0.3f;

    /// <summary>
    /// The aim at a lying spy, as a share of his collision height below his centre: the server's estimated pill pose
    /// keeps the calves and feet on the centre line from -0.5 to -0.97 (IHitZones.cs), and the body on screen lies on
    /// the floor, so a low aim hits both.
    /// </summary>
    public const float LyingAimDrop = 0.6f;

    /// <summary>A spy this close (horizontal) is at point blank: the rifle may be blocked by his body.</summary>
    public const float PointBlank = 110f;

    /// <summary>A merc whose walk is stopped by a spy's body within this slack (uu, beyond the radii) feels him.</summary>
    public const float TouchSlack = 6f;

    /// <summary>The walk counts as stopped below this speed along the pushed direction (uu/s).</summary>
    public const float BlockedSpeed = 40f;

    /// <summary>After feeling a spy the merc stops and looks at the spot this long.</summary>
    public const float FeltLookSeconds = 2f;

    /// <summary>A charge is not started with a team mate this close to its line or to the spy (the charge knocks mates down too).</summary>
    public const float ChargeMateClearance = 120f;
}

/// <summary>
/// A merc bot. Goals, scored every decision period:
/// <list type="bullet">
/// <item><b>Engage</b>: a spy it has spotted is in sight: after the reaction time it aims (the error shrinks from the
/// difficulty's first-shot spread to its settled spread while it tracks) and fires the rifle in short bursts,
/// holding fire with a teammate in the line, reloading on an empty clip.</item>
/// <item>At close quarters in Engage, the Use key as a player presses it (melee-elbow-charge-berserk.md rule 1),
/// taken by the difficulty's melee use when the controller's rules allow it: the charge with forward held at a spy 140
/// to 550 uu ahead, Use alone within elbow reach (the berserk spin when ready, else the elbow). At point blank, where
/// the spy's body blocks the muzzle, the strike is always taken, else the merc steps back to shoot.</item>
/// <item><b>Finish</b>: a spy it saw go down: walk up, face him and press Use when the game's finishing search would
/// take him (then Use again to kill at once), shooting him low until then.</item>
/// <item><b>Respond</b>: a terminal is being hacked (the HUD's objective radar and messages): run to it.</item>
/// <item><b>Frag</b>: a spy it just lost behind cover, 400 to 1600 uu away: throw a frag grenade at the spot.</item>
/// <item><b>Investigate</b>: a noise, a teammate's callout or a lost spy: go to the place, torch on in the dark,
/// motion tracking on, look round.</item>
/// <item><b>Guard</b>: the objectives left are shared out among the mercs (MercDefence); each mines its own, then
/// patrols between them, stopping at a post beside each, with the scope along a long sight line now and then.</item>
/// </list>
/// </summary>
public sealed partial class MercAi : AiBrain
{
    public const int GGuard = 0;
    public const int GEngage = 1;
    public const int GRespond = 2;
    public const int GInvestigate = 3;
    public const int GFrag = 4;
    public const int GFinish = 5;
    public const int GDefuse = 6;

    private AiContact? _target;
    private float _trackSince = -1f;
    private float _errorYaw;
    private float _errorPitch;
    private float _errorAt = -10f;
    private float _burstStart = -1f;
    private float _pauseUntil;
    private float _strafeAt;
    private bool _strafeLeft;
    private SObjectiveTrigger? _post;
    private FVector _postFloor;
    private float _postUntil;
    private float _lookAt;
    private int _lookYaw;
    private FVector _investigate;
    private AiContact? _investigating;
    private FVector _searchFloor;
    private float _investigateArrived = -1f;
    private float _fragAt = -100f;
    private int _fragPhase;
    private float _fragPhaseAt;
    private FVector _fragTarget;
    private FRotator _fragAim;
    private SComputerObjectiveTrigger? _fragObjective;
    private FVector _fragObjectivePoint;
    private float _nextObjectiveFrag;
    private SGadFragGrenade? _fragLauncher;
    private int _fragCharge;
    private float _torchAt = -100f;
    private float _visionAt = -100f;
    private SComputerObjectiveTrigger? _alert;
    private SComputerObjectiveTrigger? _respondTo;
    private FVector _respondSpot;
    private FVector _responseApproach;
    private bool _responseCover;
    private bool _respondingAsCover;
    private bool _guardTorch;
    private bool _guardMotion;
    private float _meleeRollAt = -10f;
    private bool _meleeWanted;
    private float _useAt = -10f;
    private bool _wasCharging;
    private SBasePawn? _downed;
    private float _downedSeenAt = -100f;
    private float _feltAt = -100f;
    private FVector _feltSpot;

    private static readonly Name NameCharging = new("s_PlayerCharging");
    private static readonly Name NameReloading = new("s_Reloading");
    private static readonly Name NameFullReloading = new("s_FullReloading");
    private static readonly Name NameElbowShocking = new("s_ElbowShocking");
    private static readonly Name NameReanimation = new("s_Reanimation");
    private static readonly Name NameFireGrenade = new("s_FireGrenade");
    private static readonly Name NameFiring = new("s_Firing");

    /// <summary>The controller states of a charge or a spin, where the move and the view are the state's own.</summary>
    private static readonly Name[] MeleeStates =
    [
        NameCharging, new("s_PlayerCharging_Hit"), new("s_PlayerCharging_HitEnemy"), new("s_PlayerCharging_HitFriend"),
        new("s_PlayerCharging_HitWall"), new("s_PlayerCharging_HitStickyShocked"), new("s_Berserk360"),
    ];

    public MercAi(AiController bot)
        : base(bot)
    {
        Search = new AiSearch(bot.World, bot, 1);
        _strafeLeft = bot.Personality.AimYaw < 0;
        _trapNext = bot.World.Now + bot.Personality.ScanDelay * .35f;
    }

    public AiSearch Search { get; }

    protected override bool Spy => false;

    public int Bursts { get; private set; }

    public int FragsThrown { get; private set; }

    /// <summary>Charges started (the Use key pressed with forward held and the charge allowed).</summary>
    public int Charges { get; private set; }

    /// <summary>Use presses at elbow reach (the berserk spin when it is ready, else the elbow).</summary>
    public int CloseStrikes { get; private set; }

    /// <summary>Use presses for the finishing move on a downed spy.</summary>
    public int Terminates { get; private set; }

    /// <summary>Times a spy was felt (the merc's walk stopped against him).</summary>
    public int Touches { get; private set; }

    public override string Status => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"merc {GoalName(Goal)} post {_post?.TriggerName ?? "-"} sector {_sector.Count} posts {PostsReached} traps {TrapsPlaced} scoped {ScopedShots} bursts {Bursts} charges {Charges} strikes {CloseStrikes} finishes {Terminates} frags {FragsThrown} search {Search.Inspections}/{Search.Plans} path {Follower.NextIndex}/{Follower.Path.Count}");

    public static string GoalName(int goal) => goal switch
    {
        GEngage => "engage",
        GRespond => "respond",
        GInvestigate => "investigate",
        GFrag => "frag",
        GFinish => "finish",
        GDefuse => "defuse",
        _ => "guard",
    };

    public override void Reset()
    {
        base.Reset();
        _target = null;
        _burstStart = -1f;
        _fragPhase = 0;
        _fragObjective = null;
        _post = null;
        _postUntil = 0f;
        _downed = null;
        _investigating = null;
        Search.Reset();
        _trapApproach = false;
        _postReached = false;
        _watchYaw = -1;
        _sectorAt = -100f;
        _lastPost = null;
        _visited.Clear();
    }

    // ---- choice ----------------------------------------------------------------------------------------------

    protected override int Choose(float now)
    {
        _bomb = ArmedBomb();
        if (_bomb is not null && !ImmediateBombThreat()) return GDefuse;
        if (Goal == GFrag && _fragPhase is > 0 and < 4 && now - _fragPhaseAt < 2.5f)
        {
            return GFrag;
        }

        Span<AiOption> options = stackalloc AiOption[6];
        int n = 0;
        // Mines first: until the sector has its traps, only a spy in sight, a hack or a hit takes the merc off them.
        options[n++] = new AiOption(GGuard, SettingUp(now) ? 1.2f : 0.4f + .2f * (1f - Bot.Personality.Aggression));
        _target = null;
        _investigating = null;
        AiContact? lost = null;
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            bool smoke = World.SmokeBetween(AiSight.Eye(Pawn), c.Position);
            if (((c.Visible && c.Confirmed && !smoke) ||
                MercSmokeTactics.LastSeenShot(World, AiSight.Eye(Pawn), c, now, out _, Difficulty.GadgetUse >= .5f ? 7f : 2.5f)) &&
                (c.Position - Pawn.Location).Size() < EngageRange)
            {
                if (_target is null || (c.Position - Pawn.Location).SizeSquared() < (_target.Position - Pawn.Location).SizeSquared())
                {
                    _target = c;
                }
            }
            else if ((c.Confidence > 0.2f || c.Visible && c.Spot > 0f) && (c.Visible || Search.CanSearch(c, now)) &&
                !LeftToMate(c, now) &&
                (lost is null || c.Visible && !lost.Visible || c.Visible == lost.Visible && c.Confidence > lost.Confidence))
            {
                lost = c;
            }
        }

        if (_target is not null)
        {
            options[n++] = new AiOption(GEngage, 2f);
        }

        // A spy it saw go down: finish him (the finishing move, or the rifle) before he gets up.
        if (DownedSpy(now) is { } downed)
        {
            _downed = downed;
            options[n++] = new AiOption(GFinish, 2.2f);
        }

        _alert = HackAlert();
        if (_alert is not null)
        {
            (bool assigned, _responseCover, _responseApproach) = World.RespondTo(Bot, _alert, now);
            if (assigned) options[n++] = new AiOption(GRespond, _responseCover ? 1.35f : 1.55f);
        }

        // Hurt by someone it cannot see: turn and search where the nearest known spy was, or behind.
        if (_target is null && now - Bot.HurtAt < 3f && lost is null)
        {
            _investigate = Pawn.Location - Pawn.Rotation.Vector() * 300f;
            _investigateArrived = now;
            options[n++] = new AiOption(GInvestigate, 1.1f);
        }

        if (lost is not null)
        {
            _investigating = lost;
            options[n++] = new AiOption(GInvestigate, (lost.Visible ? 0.95f : 0.55f) + 0.6f * lost.Confidence +
                (now - Bot.HurtAt < 3f ? 0.5f : 0f) + .3f * (Bot.Personality.Aggression - 1f));
            if (Goal != GInvestigate || NavFollower.Horizontal(_investigate - lost.Position) > 200f)
            {
                _investigate = lost.Position;
                _investigateArrived = -1f;
            }
        }

        if (GrenadeOpportunity(_target ?? lost, now, out FVector grenadeTarget) && Bot.Random.FRand() < Bot.Utility)
        {
            // An accepted tactical throw must beat routine investigation (including its keep-goal bonus).
            // Visible, stationary targets at range are also opportunities; rifle fire is not the only combat option.
            options[n++] = new AiOption(GFrag, _target is not null ? 2.35f : 1.45f);
            _fragTarget = grenadeTarget;
            _fragObjective = null;
        }
        else if (_target is null && _alert is { } alarm && ObjectiveGrenadeOpportunity(alarm, now, out grenadeTarget))
        {
            options[n++] = new AiOption(GFrag, 1.95f);
            _fragTarget = grenadeTarget;
            _fragObjective = alarm;
        }

        return Pick(options[..n]);
    }

    /// <summary>
    /// The nearest spy the merc saw lying on the ground (a body on the floor, as the screen shows it): in sight now, or
    /// seen lying within <see cref="MercAiK.FinishMemorySeconds"/>.
    /// </summary>
    private SBasePawn? DownedSpy(float now)
    {
        SBasePawn? best = null;
        float bestD = MercAiK.FinishRange;
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            if (c.BodyVisible && c.Pose is { Alive: true, Downed: true } pose && c.Pawn is SPawnAttaque spy &&
                (pose.Position - Pawn.Location).Size() < bestD)
            {
                bestD = (pose.Position - Pawn.Location).Size();
                best = spy;
            }
        }

        if (best is not null)
        {
            _downedSeenAt = now;
            return best;
        }

        // Out of sight now: still taken as down for a moment where he fell (a spy seen standing again is not).
        return _downed is { } last && Bot.Memory.Find(last) is { BodyVisible: false, Pose: { Alive: true, Downed: true } } &&
            now - _downedSeenAt < MercAiK.FinishMemorySeconds
            ? last
            : null;
    }

    /// <summary>A terminal being hacked, as the merc HUD's objective radar shows it (the trigger's replicated state).</summary>
    private SComputerObjectiveTrigger? HackAlert()
    {
        SComputerObjectiveTrigger? best = null;
        float bestD = float.MaxValue;
        foreach (SComputerObjectiveTrigger t in World.Terminals)
        {
            if (t.bTriggering && !World.IsTaken(t))
            {
                float d = (t.Location - Pawn.Location).Size();
                if (d < bestD)
                {
                    bestD = d;
                    best = t;
                }
            }
        }

        return best;
    }

    protected override void LeaveGoal(int from, int to, float now)
    {
        if (from == GEngage)
        {
            _burstStart = -1f;
            _trackSince = -1f;
        }

        if (from == GFrag)
        {
            _fragPhase = 0;
        }

        if (from == GFinish)
        {
            _burstStart = -1f;
            _downed = null;
        }

        if (from == GGuard)
        {
            World.Teams[1].Release(Bot);
            _post = null;
            _postUntil = 0f;
            _postReached = false;
            _watchYaw = -1;
        }

        if (from == GInvestigate)
        {
            World.Teams[1].ReleaseSearch(Bot);
        }

        Follower.Clear();
    }

    // ---- acting ----------------------------------------------------------------------------------------------

    protected override void Act(float now, ref AiPlan plan)
    {
        plan.Crouched = false;

        // A charge or a spin steers and turns the pawn itself (rules 16 and 26): no keys, the view left as it is.
        Name state = Pc.GetStateName();
        bool charging = state == NameCharging;
        if (charging && !_wasCharging)
        {
            Charges++;
        }

        _wasCharging = charging;
        if (Array.IndexOf(MeleeStates, state) >= 0)
        {
            plan.Pitch = Pc.Rotation.Pitch;
            return;
        }

        // The finishing move holds the merc (knockout-and-terminate.md rules 38 to 40): no keys, except a fresh Use
        // press while holding the spy, which kills him at once (else the 10 s timer does).
        string stateName = state.ToString();
        if (stateName.StartsWith("s_TerminatePawn", StringComparison.Ordinal))
        {
            plan.Pitch = Pc.Rotation.Pitch;
            if (stateName == "s_TerminatePawn_Maintain" && now - _useAt > 0.25f)
            {
                _useAt = now;
                plan.Keys.bUsing = 1;
            }

            return;
        }

        if (stateName.StartsWith("s_PutMineOnWall", StringComparison.Ordinal))
        {
            // The placement clip holds the merc; the stance it was started in is kept until it ends (a crouch key
            // pressed now would only flip the latch for afterwards).
            plan.Crouched = null;
            return;
        }

        if (Goal != GGuard) _trapApproach = false;

        Feel(now);
        switch (Goal)
        {
            case GEngage:
                Engage(now, ref plan);
                break;
            case GDefuse:
                Defuse(now, ref plan);
                break;
            case GRespond:
                Respond(now, ref plan);
                break;
            case GInvestigate:
                Investigate(now, ref plan);
                break;
            case GFrag:
                Frag(now, ref plan);
                break;
            case GFinish:
                Finish(now, ref plan);
                break;
            default:
                Guard(now, ref plan);
                break;
        }

        if (!Doors.Busy && Goal is not (GEngage or GFinish or GFrag) && now - _feltAt < MercAiK.FeltLookSeconds)
        {
            // Something stopped the walk: stand and look at it (down at the feet for a crouched spy).
            plan.Keys.BT_Up = 0;
            plan.Keys.BT_Down = 0;
            plan.Keys.BT_Left = 0;
            plan.Keys.BT_Right = 0;
            plan.MoveYaw = null;
            LookAt(_feltSpot, ref plan);
        }

        Reload(ref plan);
        Lights(now, ref plan);
    }

    /// <summary>
    /// A terminal is being hacked: run to a spot 200 to 600 uu from its use spot with a clear line to it, and watch the
    /// spot (where the hacker stands) on the way and there, torch on in the dark.
    /// </summary>
    private void Respond(float now, ref AiPlan plan)
    {
        if (_alert is null || !UseSpot(_alert, out FVector spot, out _))
        {
            return;
        }

        if (!ReferenceEquals(_alert, _respondTo) || _responseCover != _respondingAsCover)
        {
            _respondTo = _alert;
            _respondingAsCover = _responseCover;
            _respondSpot = Vantage(spot, _responseCover);
            Follower.Clear();
        }

        FVector watch = spot + new FVector(0f, 0f, 40f);
        MoveTo(_respondSpot, now, ref plan, sneak: false);
        if (Doors.Busy) return;
        if (World.LineOpen(AiSight.Eye(Pawn), watch))
        {
            LookAt(watch, ref plan);
        }
    }

    /// <summary>The nearest node 200 to 600 uu from <paramref name="spot"/> that sees it, or the spot itself.</summary>
    private FVector Vantage(FVector spot, bool cover)
    {
        FVector best = cover ? Feet : spot;
        FVector approach = _responseApproach - spot;
        FVector entry = new FVector(approach.X, approach.Y, 0).SafeNormal();
        NavGraph graph = Follower.Graph;
        float[] costs = ReachabilityCosts(graph.NearestNode(Pawn.Location, Pawn.CollisionHeight), maxCost: 5600f);
        float bestScore = float.MaxValue;
        List<int> nodes = NodesAround(spot, 600f);
        int stride = Math.Max(1, nodes.Count / 64);
        for (int i = 0; i < nodes.Count; i += stride)
        {
            int node = nodes[i];
            FVector f = graph.Node(node).Floor;
            FVector away = f - spot;
            float d = away.Size();
            float angle = new FVector(away.X, away.Y, 0).SafeNormal() | entry;
            if (d < (cover ? 300f : 200f) || !float.IsFinite(costs[node]) || !graph.Node(node).Standing ||
                (cover && angle > .25f) ||
                !World.LineOpen(f + new FVector(0f, 0f, 120f), spot + new FVector(0f, 0f, 40f)))
            {
                continue;
            }

            float score = costs[node] + MathF.Abs(d - (cover ? 520f : 350f));
            if (cover) score += angle * 180f - World.Patrol.Interest(f) * 130f;
            if (score < bestScore)
            {
                bestScore = score;
                best = f;
            }
        }

        return best;
    }

    private void Engage(float now, ref AiPlan plan)
    {
        AiContact? c = _target;
        if (c is null) return;
        if (World.SmokeBetween(AiSight.Eye(Pawn), c.Position))
        {
            if (MercSmokeTactics.LastSeenShot(World, AiSight.Eye(Pawn), c, now, out FVector remembered))
            {
                float blindSpread = Spread(now, 3f);
                Aim(remembered, ref plan);
                Shoot(now, remembered, blindSpread, ref plan);
            }
            else if (Difficulty.GadgetUse >= .5f && MercSmokeTactics.LastSeenShot(World, AiSight.Eye(Pawn), c, now, out remembered, 7f))
            {
                // Give the laser time to tag someone still at the remembered spot; no unseen movement is followed.
                LookAt(remembered, ref plan);
            }
            return;
        }
        if (!c.Visible || c.BodyVisible && c.Pose is { Alive: false })
        {
            return;
        }

        if (c.BodyVisible && c.Pose is { Downed: true } && c.Pawn is SPawnAttaque down)
        {
            // He went down in front of the merc (an elbow, a charge, a spin): finish him.
            _downed = down;
            _downedSeenAt = now;
            Finish(now, ref plan);
            return;
        }

        if (_trackSince < 0f)
        {
            _trackSince = now;
        }

        FVector aimPoint = c.Position - new FVector(0f, 0f, c.Pose is { Downed: true } p ? p.CollisionHeight * MercAiK.LyingAimDrop : Bot.Personality.AimHeight);
        aimPoint += c.Velocity * (Bot.ReactionTime * 0.5f * Difficulty.LeadFactor);

        // Seen is not shootable: glass and grates show a spy and stop the bullet. With the chest covered the head is
        // tried; with both covered the merc holds its fire and walks to a spot with a clear shot, watching him.
        if (ShotBlocked(aimPoint))
        {
            FVector head = c.Position + new FVector(0f, 0f, c.Pose is { Downed: false } standing ? standing.CollisionHeight * 0.7f : 55f);
            if (c.Pose is { Downed: true } || ShotBlocked(head))
            {
                _burstStart = -1f;
                _trackSince = -1f;
                if (now - _clearSpotAt > 1.5f)
                {
                    _clearSpotAt = now;
                    _clearSpotFound = ClearShotSpot(c.Position, out _clearSpot);
                }

                if (_clearSpotFound && (Feet - _clearSpot).Size() > 60f)
                {
                    MoveTo(_clearSpot, now, ref plan, sneak: false);
                    if (Doors.Busy) return;
                }

                LookAt(c.Position, ref plan);
                HeldShots++;
                return;
            }

            aimPoint = head;
        }

        // A spy far off is taken through the scope: no spread, no falloff, one shot at a time.
        bool scope = ScopeWanted(c, now);
        bool scoped = Scoped;
        float spread = Spread(now, scope && scoped ? AiK.ScopeSpreadFactor : 1f);
        Aim(aimPoint, ref plan);
        if (scope)
        {
            // Standing still while the scope comes up and through every shot.
            _scopeWantAt = now;
            _burstStart = -1f;
            if (scoped && c.BodyVisible && c.VisibleSince >= 0f && now - c.VisibleSince >= Bot.ReactionTime)
            {
                ScopedShot(now, aimPoint, spread, ref plan);
            }

            return;
        }

        // Movement: the better mercs side-step between bursts at range.
        if (Difficulty.Skill >= AiSkill.Hard && (c.Position - Pawn.Location).Size() > 600f)
        {
            if (now - _strafeAt > 0.8f * Bot.Personality.Tempo)
            {
                _strafeAt = now;
                _strafeLeft = !_strafeLeft;
            }

            if (_strafeLeft)
            {
                plan.Keys.BT_Left = 1;
            }
            else
            {
                plan.Keys.BT_Right = 1;
            }
        }

        // At point blank the spy's body can block the muzzle (CheckIfCanFire, merc-rifle.md rule 3): the rifle does not
        // fire, as a player sees; the elbow or the spin then, else a step back to shoot.
        bool pointBlank = NavFollower.Horizontal(c.Position - Pawn.Location) < MercAiK.PointBlank;
        bool muzzleBlocked = pointBlank && !Pc.CheckIfCanFire();
        bool reacted = c.VisibleSince >= 0f && now - c.VisibleSince >= Bot.ReactionTime;
        if (reacted && c.BodyVisible && c.Pawn is SBasePawn spy && Melee(now, spy, muzzleBlocked, ref plan))
        {
            _burstStart = -1f;
            return;
        }

        if (muzzleBlocked)
        {
            plan.Keys.BT_Down = 1;
            plan.Keys.BT_Up = 0;
            _burstStart = -1f;
            return;
        }

        if (!reacted)
        {
            _burstStart = -1f;
            return;
        }

        Shoot(now, aimPoint, spread, ref plan);
    }

    /// <summary>The aim error: a fresh draw every 0.25 s, from the first-shot spread to the settled one while tracking.</summary>
    private float Spread(float now, float multiplier = 1f)
    {
        float tracked = _trackSince < 0f ? 0f : now - _trackSince;
        float spread = SvmMath.Lerp(Difficulty.AimErrorDegrees, Difficulty.AimSettledDegrees,
            SvmMath.Clamp(tracked / SvmMath.Max(0.05f, Difficulty.AimSettleSeconds), 0f, 1f));
        if (multiplier > 1f) spread = MathF.Max(8f, spread * multiplier);
        else if (multiplier < 1f) spread *= multiplier;
        if (now - _errorAt > 0.25f * Bot.Personality.Tempo)
        {
            _errorAt = now;
            _errorYaw = (Bot.Random.FRand() + Bot.Random.FRand() - 1f) * spread;
            _errorPitch = (Bot.Random.FRand() + Bot.Random.FRand() - 1f) * spread * 0.6f;
        }

        return spread;
    }

    /// <summary>The view on <paramref name="point"/> with the current aim error.</summary>
    private void Aim(FVector point, ref AiPlan plan)
    {
        FRotator aim = (point - AiSight.Eye(Pawn)).Rotation();
        const float unitsPerDegree = 65536f / 360f;
        if (plan.Keys.BT_Up != 0 && plan.Yaw is { } moving && plan.MoveYaw is null)
        {
            // A walk already planned goes on in its own direction (camera-relative keys).
            plan.MoveYaw = moving;
        }

        // The bot's own aiming habit (a constant offset) shrinks through the scope as its spread does.
        int habit = Bot.Personality.AimYaw / 8;
        if (Scoped) habit = (int)(habit * AiK.ScopeSpreadFactor);
        plan.Yaw = (aim.Yaw + (int)(_errorYaw * unitsPerDegree) + habit) & 0xFFFF;
        plan.Pitch = aim.Pitch + (int)(_errorPitch * unitsPerDegree);
        plan.Tolerance = 65536;
    }

    /// <summary>Short bursts at <paramref name="target"/> while the view is on it, not through a team mate, with a loaded clip.</summary>
    private void Shoot(float now, FVector target, float spread, ref AiPlan plan)
    {
        float error = AimErrorTo(target);
        bool onTarget = error < spread + 3f;
        // No burst into cover: the remembered spot of a blind shot through smoke can lie behind a wall or glass too.
        if (!onTarget || TeammateInTheWay(target) || Pawn.MyWeapon is not SWeaponDefense w || w.ClipAmmo <= 0 || ShotBlocked(target))
        {
            _burstStart = -1f;
            return;
        }

        if (now < _pauseUntil)
        {
            return;
        }

        if (_burstStart < 0f)
        {
            _burstStart = now;
            Bursts++;
            plan.Keys.Press(AiCommand.Fire);
            plan.Keys.bFire = 1;
            return;
        }

        if (now - _burstStart < AiK.BurstSeconds * Bot.Personality.Burst)
        {
            plan.Keys.bFire = 1;
            return;
        }

        _burstStart = -1f;
        _pauseUntil = now + AiK.BurstPause * Bot.Personality.Tempo * (Difficulty.Skill >= AiSkill.Hard ? 0.7f : 1.2f);
    }

    /// <summary>
    /// A spy lies on the ground: walk up to him, turn to him and press Use when the game's finishing search would
    /// take him (FindPawnToReanimateOrTerminate, knockout-and-terminate.md rule 36); shoot him (low) while he is in
    /// sight and the press is not there yet.
    /// </summary>
    private void Finish(float now, ref AiPlan plan)
    {
        if (_downed is not { } spy || Bot.Memory.Find(spy) is not { Pose: { Alive: true, Downed: true } pose } c)
        {
            return;
        }

        bool seen = c.BodyVisible;
        FVector at = pose.Position;
        bool known = seen || now - _downedSeenAt < MercAiK.FinishMemorySeconds;
        if (!known)
        {
            return;
        }

        if (TerminateReady(pose))
        {
            // No stick on the press: Use with forward is the charge.
            LookAt(pose.Body, ref plan);
            plan.Keys = default;
            plan.MoveYaw = null;
            if (now - _useAt > 0.25f)
            {
                _useAt = now;
                Terminates++;
                plan.Keys.bUsing = 1;
            }

            return;
        }

        FVector feet = pose.Feet;
        FVector away = new FVector(Pawn.Location.X - at.X, Pawn.Location.Y - at.Y, 0f).SafeNormal();
        if (NavFollower.Horizontal(at - Pawn.Location) > MercAiK.FinishWalkUntil)
        {
            int node = Follower.Graph.NearestNode(feet + (away * 100f) + new FVector(0f, 0f, 60f), 0f, 300f);
            if (node < 0 || !MoveTo(Follower.Graph.Node(node).Floor, now, ref plan, sneak: false))
            {
                plan.Yaw = NavFollower.YawTo(Pawn.Location, at);
                plan.Tolerance = 3000;
                plan.Keys.BT_Up = World.LineOpen(Pawn.Location, at) ? (byte)1 : (byte)0;
            }

            if (Doors.Busy) return;
        }

        // Keep the camera on the fallen body while approaching it, including when the last sight was a moment ago.
        // Looking at the cylinder centre/horizon made the prone body fall outside the view at close range.
        LookAt(pose.Body, ref plan);
        if (!seen)
        {
            return;
        }

        if (_trackSince < 0f)
        {
            _trackSince = now;
        }

        float spread = Spread(now);
        Aim(pose.Body, ref plan);
        if (NavFollower.Horizontal(at - Pawn.Location) < MercAiK.PointBlank && !Pc.CheckIfCanFire())
        {
            // The body under the muzzle (the finishing search refused here: a step, a wall): a step back to shoot.
            plan.Keys.BT_Down = 1;
            plan.Keys.BT_Up = 0;
            plan.MoveYaw = null;
            return;
        }

        Shoot(now, pose.Body, spread, ref plan);
    }

    /// <summary>
    /// The finishing search's own test for <paramref name="spy"/> (FindPawnToReanimateOrTerminate, rule 36), with a
    /// margin: in a walking state that acts on the Use press, no menu entry to open first, the spy alive and
    /// finishable, within 250 uu and in front of the body, the feet within 10 uu in height and the clearance box free
    /// from the merc to the spot 110 uu behind the spy.
    /// </summary>
    private bool TerminateReady(AiObservedPose spy)
    {
        if (Pawn is not SPAWNDEFENSE me || !spy.Finishable || !spy.Alive || me.Physics != EPhysics.PHYS_Walking ||
            !Pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) ||
            (Pc.IManager?.HasUsableInteractions() ?? false) || me.bStickyShocking)
        {
            return false;
        }

        FVector to = spy.Position - me.Location;
        if (to.Size() >= K.ReviveRange - MercAiK.ReachMargin || (to.SafeNormal() | me.Rotation.Vector().SafeNormal()) <= MercAiK.TerminateFacingDot)
        {
            return false;
        }

        FVector feet = spy.Feet;
        if (MathF.Abs(me.Location.Z - me.CollisionHeight - feet.Z) >= K.TerminateHeightTolerance)
        {
            return false;
        }

        if (TraceHitZones.CollisionOf(Pc) is not { } world)
        {
            return true;
        }

        FVector spot = feet - spy.Rotation.Vector() * K.TerminateStandOffset;
        FVector from = me.Location - new FVector(0f, 0f, me.CollisionHeight - K.TerminateClearanceRaise);
        return !world.LineCheck(out _, me, spot + new FVector(0f, 0f, K.TerminateClearanceRaise), from, K.TerminateClearanceExtent,
            ETraceFlags.TRACE_AllColliding);
    }

    /// <summary>
    /// The merc's walk stopped against a spy's body (a player sees his walk blocked by something): the spot is noted
    /// as a sure contact and the merc stops and looks there. A spy who keeps still beside a merc that stands still
    /// gives no cue.
    /// </summary>
    private void Feel(float now)
    {
        if (Pawn.Physics != EPhysics.PHYS_Walking)
        {
            return;
        }

        FVector push = new(Pawn.Acceleration.X, Pawn.Acceleration.Y, 0f);
        if (push.SizeSquared() < 1f)
        {
            return;
        }

        push = push.SafeNormal();
        if ((Pawn.Velocity | push) > MercAiK.BlockedSpeed)
        {
            return;
        }

        foreach (Pawn p in Bot.Enemies)
        {
            // A lying body does not block (collision (1, 0, 0)).
            if (p is not SBasePawn { bLyingOnGround: false } spy)
            {
                continue;
            }

            FVector to = spy.Location - Pawn.Location;
            float flat = NavFollower.Horizontal(to);
            if (flat > Pawn.CollisionRadius + spy.CollisionRadius + MercAiK.TouchSlack ||
                MathF.Abs(to.Z) > Pawn.CollisionHeight + spy.CollisionHeight ||
                (new FVector(to.X, to.Y, 0f).SafeNormal() | push) < 0.5f)
            {
                continue;
            }

            if (now - _feltAt > MercAiK.FeltLookSeconds)
            {
                Touches++;
            }

            _feltAt = now;
            _feltSpot = spy.Location;
            Bot.Memory.Report(spy, spy.Location, now, 0.9f, AiContactSource.Heard);
            return;
        }
    }

    /// <summary>
    /// The Use key at close quarters (melee-elbow-charge-berserk.md rule 1), pressed only when the controller would
    /// take it: a charge (forward held) needs the spy 140 to 550 uu ahead in the view and the charge cone, a clear
    /// line, the charge and spin cooldowns and no reload; Use alone needs the spy within elbow reach in front, out of
    /// his wake protection, and no team mate near (the spin and the elbow hit team mates too). Each chance is taken by
    /// the difficulty's melee use, looked at once a second; the better mercs never charge or strike a spy holding
    /// his shocker on them (rule 19: the merc would shock himself). True when Use is pressed this tick.
    /// </summary>
    private bool Melee(float now, SBasePawn spy, bool forced, ref AiPlan plan)
    {
        if (Difficulty.MeleeUse <= 0f || Pawn is not SPAWNDEFENSE merc || merc.bIsCrouched || merc.Physics != EPhysics.PHYS_Walking ||
            !Pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) ||
            (Pc.IManager?.HasUsableInteractions() ?? false) || (spy.bStickyShocking && Difficulty.Skill >= AiSkill.Hard))
        {
            return false;
        }

        FVector to = spy.Location - merc.Location;
        float dist = to.Size();
        GameReplicationInfo? gri = Pc.GameReplicationInfo;
        bool charge = dist > AiK.MercChargeMin && dist < AiK.MercChargeReach && ChargeAllowed(now, merc, gri) &&
            AimErrorTo(spy.Location) < AiK.MercChargeAimDegrees && World.LineOpen(merc.Location, spy.Location) &&
            World.LineOpen(AiSight.Eye(merc), spy.Location) && !MateNearLine(merc.Location, spy.Location, MercAiK.ChargeMateClearance);
        bool strike = !charge && dist < AiK.MercElbowReach && (to.SafeNormal() | merc.Rotation.Vector()) > K.ElbowMercCone + 0.1f &&
            !spy.bStickyShocking && now - spy.iLastWakeTime > K.ElbowWakeProtection && StrikeAllowed(now, merc, gri) && !MateNear(merc);
        if (!charge && !strike)
        {
            return false;
        }

        if (now >= _meleeRollAt)
        {
            _meleeRollAt = now + AiK.MeleeRollSeconds;
            _meleeWanted = Bot.Random.FRand() < Difficulty.MeleeUse;
        }

        // Use needs a fresh press (the edge of BT_UseJusteAppuye): released for a moment between two. A strike whose
        // rifle is blocked by the spy's body is not left to the roll (a player has nothing else at that range).
        if (!(_meleeWanted || (forced && strike)) || now - _useAt < 0.25f)
        {
            return false;
        }

        _useAt = now;
        plan.Keys = default;
        plan.Keys.bUsing = 1;
        plan.Tolerance = 65536;
        plan.MoveYaw = null;
        if (charge)
        {
            // The charge needs the stick pushed past fPadXMaxLu * fChargeSeuilPad (250): the forward key gives 6000.
            plan.Keys.BT_Up = 1;
        }
        else
        {
            CloseStrikes++;
        }

        return true;
    }

    /// <summary>The charge test of the Use edge (rule 1) as the controller makes it; the target lock is the view's line up.</summary>
    private bool ChargeAllowed(float now, SPAWNDEFENSE merc, GameReplicationInfo? gri)
    {
        float since = now - Pc.fLastChargingTime;
        return (gri is null || gri.bAvlb_MERC_Charge) && Pc.CanChargeAfterBerserk &&
            !(merc.MyWeapon?.IsInState(NameReloading) ?? false) &&
            (since > Pc.iSecondBetweenTwoChargesWithoutEnnemy || since > Pc.iSecondBetweenTwoChargesWithEnnemy);
    }

    /// <summary>The spin's weapon test, else the elbow's (rules 1 and 4): either answers the Use key without a whiff.</summary>
    private bool StrikeAllowed(float now, SPAWNDEFENSE merc, GameReplicationInfo? gri)
    {
        if (merc.MyWeapon is not { } rifle)
        {
            return false;
        }

        bool spin = (gri is null || gri.bAvlb_MERC_Berserk) && now - Pc.fLastBerserkTime > Pc.fSecondBetweenTwoBerserks &&
            now - Pc.fLastChargingTime > Pc.fSecondBetweenChargeAndBerserk && !rifle.IsInState(NameReloading) &&
            !rifle.IsInState(NameFullReloading) && !rifle.IsInState(NameElbowShocking) && !rifle.IsInState(NameReanimation) &&
            !rifle.IsInState(NameFireGrenade);
        bool elbow = (gri is null || gri.bAvlb_MERC_Elbow) && !rifle.IsInState(NameReloading) && !rifle.IsInState(NameFiring);
        return spin || elbow;
    }

    /// <summary>A living team mate within <paramref name="clearance"/> of the segment, or of its end (the charge's path and its victim).</summary>
    private bool MateNearLine(FVector from, FVector to, float clearance)
    {
        FVector d = to - from;
        float len2 = d.SizeSquared();
        foreach (SPAWNDEFENSE mate in World.Level.DynamicActors<SPAWNDEFENSE>())
        {
            if (ReferenceEquals(mate, Pawn) || mate.bDeleteMe || mate.Health <= 0)
            {
                continue;
            }

            float t = len2 > 0f ? SvmMath.Clamp(((mate.Location - from) | d) / len2, 0f, 1f) : 0f;
            if ((from + d * t - mate.Location).SizeSquared() < clearance * clearance)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A living team mate within the spin's clearance.</summary>
    private bool MateNear(SPAWNDEFENSE merc)
    {
        foreach (SPAWNDEFENSE mate in World.Level.DynamicActors<SPAWNDEFENSE>())
        {
            if (!ReferenceEquals(mate, merc) && mate.Health > 0 && (mate.Location - merc.Location).Size() < AiK.MercSpinClearance)
            {
                return true;
            }
        }

        return false;
    }

    private bool TeammateInTheWay(FVector target)
    {
        FVector eye = AiSight.Eye(Pawn);
        FVector d = target - eye;
        float len2 = d.SizeSquared();
        if (len2 <= 0f)
        {
            return false;
        }

        foreach (SPAWNDEFENSE mate in World.Level.DynamicActors<SPAWNDEFENSE>())
        {
            if (ReferenceEquals(mate, Pawn) || mate.bDeleteMe || mate.Health <= 0)
            {
                continue;
            }

            float t = ((mate.Location - eye) | d) / len2;
            if (t > 0f && t < 1f && (eye + d * t - mate.Location).SizeSquared() < AiK.FriendlyClearance * AiK.FriendlyClearance)
            {
                return true;
            }
        }

        return false;
    }

    private void Reload(ref AiPlan plan)
    {
        if (Goal != GEngage && Pawn.MyWeapon is SWeaponDefense { ClipAmmo: <= 0, Ammo: > 0 })
        {
            plan.Keys.bReload = 1;
        }
    }

    private void Investigate(float now, ref AiPlan plan)
    {
        if (_investigating is { Visible: true } shape)
        {
            // Inspect a shape before committing to an attack. Walking through it while waiting to identify it
            // let close spies pass behind the camera and made the merc miss its melee opportunity.
            LookAt(shape.Position, ref plan);
            return;
        }

        if (_investigating is { } contact)
        {
            if (!Search.Next(contact, Pawn, Pc, Difficulty, now, out FVector searchFloor))
            {
                Follower.Clear();
                return;
            }

            if ((_searchFloor - searchFloor).SizeSquared() > 40f * 40f || !Follower.HasGoal)
            {
                _searchFloor = searchFloor;
                _investigateArrived = -1f;
            }

            FVector point = searchFloor + new FVector(0f, 0f, AiK.SpyCrouchHeight);
            if (_investigateArrived < 0f)
            {
                if (!MoveTo(searchFloor, now, ref plan, sneak: false))
                {
                    Search.SkipGoal();
                    return;
                }

                // Door handling owns the view as well as Use; watching the search spot prevents facing its button.
                if (Doors.Busy) return;

                if (Follower.Arrived || (Feet - searchFloor).Size() < 80f)
                {
                    _investigateArrived = now;
                }
            }

            LookAt(point, ref plan);
            if (_investigateArrived >= 0f && now - _investigateArrived > 1f)
            {
                LookRound(now, ref plan, point);
                if (now - _investigateArrived > 2f)
                {
                    Search.SkipGoal();
                }
            }

            return;
        }

        int node = Follower.Graph.NearestNode(_investigate + new FVector(0f, 0f, 60f), 0f, 300f);
        if (node < 0)
        {
            LookRound(now, ref plan, _investigate);
            return;
        }

        FVector floor = Follower.Graph.Node(node).Floor;
        if (_investigateArrived < 0f)
        {
            MoveTo(floor, now, ref plan, sneak: false);
            if (Doors.Busy) return;
            if (Follower.Arrived || Follower.Failed || (Feet - floor).Size() < 80f)
            {
                _investigateArrived = now;
            }

            // Look where the noise was while closing in, once there is a line to it.
            if (World.LineOpen(AiSight.Eye(Pawn), _investigate + new FVector(0f, 0f, 40f)) && (Pawn.Location - _investigate).Size() < 900f)
            {
                LookAt(_investigate, ref plan);
            }

            return;
        }

        LookRound(now, ref plan, _investigate);
    }

    /// <summary>Looks round a point of interest: a new direction every 1.5 to 3 s, biased toward <paramref name="focus"/>.</summary>
    private void LookRound(float now, ref AiPlan plan, FVector focus)
    {
        if (now >= _lookAt)
        {
            _lookAt = now + (1.5f + Bot.Random.FRand() * 1.5f) * Bot.Personality.Tempo;
            int toward = NavFollower.YawTo(Pawn.Location, focus) + Bot.Personality.AimYaw;
            int spreadUnits = (int)(Bot.Random.RandRange(-1f, 1f) * 20000f);
            _lookYaw = Bot.Random.FRand() < 0.5f ? (toward + 32768 + spreadUnits) & 0xFFFF : (toward + spreadUnits / 2) & 0xFFFF;
        }

        plan.Yaw = _lookYaw;
        plan.Pitch = 0;
        plan.Tolerance = 65536;
    }

    private void Frag(float now, ref AiPlan plan)
    {
        FRotator aim = _fragAim;
        if (_fragPhase < 2)
        {
            bool safe = _fragObjective is { } objective
                ? objective.bTriggering && !World.IsTaken(objective) &&
                  SolveFragAim(_fragTarget, _fragLauncher?.ProjectileDefaults, out aim, out _, out _, out _)
                : GrenadeAim(_fragTarget, out aim) && GrenadeBlastClear(_fragTarget);
            if (!safe)
            {
                _fragPhase = 4;
                _fragAt = now - 8f;
                return;
            }

            _fragAim = aim;
        }

        // Once AltFire is accepted, release and observe the actual use even if a teammate or door moves.
        // Retargeting/aborting now cannot recall the grenade and must not bypass its count or cooldown.
        plan.Yaw = aim.Yaw;
        plan.Pitch = aim.Pitch;
        plan.Tolerance = 65536;
        switch (_fragPhase)
        {
            case 0:
                int slot = GadgetSlot<SGadFragGrenade>(out _fragLauncher);
                if (slot < 0)
                {
                    return;
                }

                if (_fragObjective is { } alarm && !World.Teams[1].ReserveObjectiveFrag(alarm, Bot, now))
                {
                    _fragPhase = 4;
                    _fragAt = now - 8f;
                    return;
                }

                if (!ReferenceEquals(Pawn.SelectedGadget, _fragLauncher))
                {
                    plan.Keys.Press(AiCommand.SelectGadget, slot);
                }

                _fragCharge = _fragLauncher!.Charge;
                _fragPhase = 1;
                _fragPhaseAt = now;
                return;
            case 1:
                if (!ReferenceEquals(Pawn.SelectedGadget, _fragLauncher) || !GrenadeWeaponReady() ||
                    now - _fragPhaseAt < 0.3f || NavFollower.YawError(Pc.Rotation.Yaw & 0xffff, aim.Yaw & 0xffff) > (_fragObjective is null ? 1000 : 125) ||
                    NavFollower.YawError(Pc.Rotation.Pitch & 0xffff, aim.Pitch & 0xffff) > (_fragObjective is null ? 1000 : 125) ||
                    NavFollower.Horizontal(Pawn.Velocity) > 5f)
                {
                    if (now - _fragPhaseAt > 2f)
                    {
                        _fragPhase = 4;
                        _fragAt = now - 8f;
                    }

                    return;
                }

                if (_fragObjective is not null && !TryObjectiveArc(_fragTarget, _fragObjectivePoint, out _))
                {
                    _fragPhase = 4;
                    _fragAt = now - 8f;
                    return;
                }

                plan.Keys.Press(AiCommand.AltFire);
                plan.Keys.bAltFire = 1;
                _fragPhase = 2;
                _fragPhaseAt = now;
                return;
            case 2:
                // Release within 0.25 s: a full-strength throw.
                if (now - _fragPhaseAt < 0.1f)
                {
                    plan.Keys.bAltFire = 1;
                    return;
                }

                _fragPhase = 3;
                _fragPhaseAt = now;
                return;
            default:
                if (_fragPhase == 3 && _fragLauncher is { } thrown && thrown.Charge < _fragCharge)
                {
                    FragsThrown++;
                    _fragAt = now;
                    _fragPhase = 4;
                    return;
                }

                if (_fragPhase == 3 && now - _fragPhaseAt > 1.8f)
                {
                    _fragPhase = 4;
                    _fragAt = now - 8f;
                }

                return;
        }
    }

    /// <summary>The torch in the dark and motion tracking while searching, by the difficulty's gadget use.</summary>
    private void Lights(float now, ref AiPlan plan)
    {
        // The scope state ignores the torch and laser keys.
        if (Difficulty.GadgetUse < 0.15f || Pc.bBinocularVision)
        {
            return;
        }

        bool smokeShot = _target is { } target &&
            MercSmokeTactics.LastSeenShot(World, AiSight.Eye(Pawn), target, now, out _, 7f);
        if (Pawn is SPAWNDEFENSE { TorcheLaser: { } laser } && now - _torchAt > .5f)
        {
            bool wantLaser = smokeShot && Difficulty.GadgetUse >= .5f;
            if (wantLaser != laser.bInUse)
            {
                plan.Keys.Press(AiCommand.GadLaserSwitch);
                _torchAt = now;
                return;
            }
        }
        if (smokeShot || _trapApproach) return;

        bool searching = Goal is GInvestigate or GRespond or GFinish;
        int mode = AiSight.RendMapOf(Pc);
        if ((now - _visionAt > 4f || Goal == GFinish && now - _visionAt > 0.75f) && Goal != GEngage)
        {
            // Motion tracking for a noise (something moved there) and on some dark posts; the torch (normal view)
            // for a terminal alert, since a hacker stands still and motion tracking does not show it.
            bool wantMotion = (Goal == GInvestigate || (Goal == GGuard && _guardMotion)) && Difficulty.GadgetUse >= 0.5f &&
                World.LightAround(Feet) < 0.2f;
            if (wantMotion != (mode == SGoggle.REN_MotionTrackingVision) && plan.Keys.Command2 == AiCommand.None)
            {
                plan.Keys.Press(AiCommand.SwitchHeadset, 1);
                _visionAt = now;
                return;
            }
        }

        if (mode != SGoggle.REN_DynLight || now - _torchAt < 3f || Pawn is not SPAWNDEFENSE merc || merc.Torche is not { } torch)
        {
            return;
        }

        // On below 0.15, off only above 0.35, so the torch does not flicker at the edge of a light.
        bool dark = World.LightAround(Feet) < (torch.bInUse ? 0.35f : 0.15f);
        bool wantTorch = dark && (searching || Goal == GEngage || _guardTorch);
        if (wantTorch != torch.bInUse)
        {
            plan.Keys.Press(AiCommand.GadTorcheSwitch);
            _torchAt = now;
        }
    }
}
