using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

/// <summary>
/// A merc bot. Goals, scored every decision period:
/// <list type="bullet">
/// <item><b>Engage</b>: a spy it has spotted is in sight: after the reaction time it aims (the error shrinks from the
/// difficulty's first-shot spread to its settled spread while it tracks) and fires the rifle in short bursts,
/// holding fire with a teammate in the line, reloading on an empty clip.</item>
/// <item>At close quarters in Engage, the Use key as a player presses it (melee-elbow-charge-berserk.md rule 1),
/// taken by the difficulty's melee use when the controller's rules allow it: the charge with forward held at a spy 140
/// to 550 uu ahead, Use alone within elbow reach (the berserk spin when ready, else the elbow).</item>
/// <item><b>Respond</b>: a terminal is being hacked (the HUD's objective radar and messages): run to it.</item>
/// <item><b>Frag</b>: a spy it just lost behind cover, 400 to 1600 uu away: throw a frag grenade at the spot.</item>
/// <item><b>Investigate</b>: a noise, a teammate's callout or a lost spy: go to the place, torch on in the dark,
/// motion tracking on, look round.</item>
/// <item><b>Guard</b>: a post beside an untaken terminal no teammate holds; look round there, change post now and then.</item>
/// </list>
/// </summary>
public sealed class MercAi : AiBrain
{
    public const int GGuard = 0;
    public const int GEngage = 1;
    public const int GRespond = 2;
    public const int GInvestigate = 3;
    public const int GFrag = 4;

    private AiContact? _target;
    private float _trackSince = -1f;
    private float _errorYaw;
    private float _errorPitch;
    private float _errorAt = -10f;
    private float _burstStart = -1f;
    private float _pauseUntil;
    private float _strafeAt;
    private bool _strafeLeft;
    private SComputerObjectiveTrigger? _post;
    private FVector _postFloor;
    private float _postUntil;
    private float _lookAt;
    private int _lookYaw;
    private FVector _investigate;
    private float _investigateArrived = -1f;
    private float _fragAt = -100f;
    private int _fragPhase;
    private float _fragPhaseAt;
    private FVector _fragTarget;
    private float _torchAt = -100f;
    private float _visionAt = -100f;
    private SComputerObjectiveTrigger? _alert;
    private SComputerObjectiveTrigger? _respondTo;
    private FVector _respondSpot;
    private bool _guardTorch;
    private bool _guardMotion;
    private float _meleeRollAt = -10f;
    private bool _meleeWanted;
    private float _useAt = -10f;
    private bool _wasCharging;

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
    }

    protected override bool Spy => false;

    public int Bursts { get; private set; }

    public int FragsThrown { get; private set; }

    /// <summary>Charges started (the Use key pressed with forward held and the charge allowed).</summary>
    public int Charges { get; private set; }

    /// <summary>Use presses at elbow reach (the berserk spin when it is ready, else the elbow).</summary>
    public int CloseStrikes { get; private set; }

    public override string Status => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"merc {GoalName(Goal)} post {_post?.TriggerName ?? "-"} bursts {Bursts} charges {Charges} strikes {CloseStrikes} frags {FragsThrown} path {Follower.NextIndex}/{Follower.Path.Count}");

    public static string GoalName(int goal) => goal switch
    {
        GEngage => "engage",
        GRespond => "respond",
        GInvestigate => "investigate",
        GFrag => "frag",
        _ => "guard",
    };

    public override void Reset()
    {
        base.Reset();
        _target = null;
        _burstStart = -1f;
        _fragPhase = 0;
        _post = null;
    }

    // ---- choice ----------------------------------------------------------------------------------------------

    protected override int Choose(float now)
    {
        if (Goal == GFrag && _fragPhase is > 0 and < 4 && now - _fragPhaseAt < 2.5f)
        {
            return GFrag;
        }

        Span<AiOption> options = stackalloc AiOption[5];
        int n = 0;
        options[n++] = new AiOption(GGuard, 0.4f);
        _target = null;
        AiContact? lost = null;
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            if (c.Visible && c.Confirmed && (c.Pawn.Location - Pawn.Location).Size() < AiK.MercEngageRange)
            {
                if (_target is null || (c.Pawn.Location - Pawn.Location).SizeSquared() < (_target.Pawn.Location - Pawn.Location).SizeSquared())
                {
                    _target = c;
                }
            }
            else if (c.Confidence > 0.2f && (lost is null || c.Confidence > lost.Confidence))
            {
                lost = c;
            }
        }

        if (_target is not null)
        {
            options[n++] = new AiOption(GEngage, 2f);
        }

        _alert = HackAlert();
        if (_alert is not null)
        {
            options[n++] = new AiOption(GRespond, 1.3f);
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
            options[n++] = new AiOption(GInvestigate, 0.55f + 0.6f * lost.Confidence + (now - Bot.HurtAt < 3f ? 0.5f : 0f));
            float d = (lost.Position - Pawn.Location).Size();
            if (lost.Source is AiContactSource.Seen or AiContactSource.Told && now - lost.Time < 4f && lost.Confidence > 0.45f &&
                d is > 400f and < 1600f && now - _fragAt > 10f &&
                !World.LineOpen(AiSight.Eye(Pawn), lost.Position) && GadgetSlot<SGadFragGrenade>(out SGadFragGrenade? frag) >= 0 &&
                frag is { Charge: > 0 } && Bot.Random.FRand() < Difficulty.GadgetUse)
            {
                options[n++] = new AiOption(GFrag, 0.9f + 0.3f * Difficulty.GadgetUse);
                _fragTarget = lost.Position;
            }

            if (Goal != GInvestigate || NavFollower.Horizontal(_investigate - lost.Position) > 200f)
            {
                _investigate = lost.Position;
                _investigateArrived = -1f;
            }
        }

        return Pick(options[..n]);
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

        // A teammate nearer to it goes; the others keep their posts (with teamwork).
        if (best is not null && Difficulty.Teamwork > 0.3f)
        {
            foreach (SPAWNDEFENSE mate in World.Level.DynamicActors<SPAWNDEFENSE>())
            {
                if (!ReferenceEquals(mate, Pawn) && mate.Health > 0 && (mate.Location - best.Location).Size() < bestD * 0.6f)
                {
                    return null;
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

        if (from == GGuard)
        {
            World.Teams[1].Release(Bot);
            _post = null;
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

        switch (Goal)
        {
            case GEngage:
                Engage(now, ref plan);
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
            default:
                Guard(now, ref plan);
                break;
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

        if (!ReferenceEquals(_alert, _respondTo))
        {
            _respondTo = _alert;
            _respondSpot = Vantage(spot);
            Follower.Clear();
        }

        FVector watch = spot + new FVector(0f, 0f, 40f);
        MoveTo(_respondSpot, now, ref plan, sneak: false);
        if (World.LineOpen(AiSight.Eye(Pawn), watch))
        {
            LookAt(watch, ref plan);
        }
    }

    /// <summary>The nearest node 200 to 600 uu from <paramref name="spot"/> that sees it, or the spot itself.</summary>
    private FVector Vantage(FVector spot)
    {
        FVector best = spot;
        float bestScore = float.MaxValue;
        List<int> nodes = NodesAround(spot, 600f);
        int stride = Math.Max(1, nodes.Count / 64);
        for (int i = 0; i < nodes.Count; i += stride)
        {
            FVector f = Follower.Graph.Node(nodes[i]).Floor;
            float d = (f - spot).Size();
            if (d < 200f || !World.LineOpen(f + new FVector(0f, 0f, 120f), spot + new FVector(0f, 0f, 40f)))
            {
                continue;
            }

            float score = (f - Pawn.Location).Size() + MathF.Abs(d - 400f);
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
        if (c is null || c.Pawn.Health <= 0 || !c.Visible)
        {
            return;
        }

        if (_trackSince < 0f)
        {
            _trackSince = now;
        }

        // The aim error: a fresh draw every 0.25 s, from the first-shot spread to the settled one while tracking.
        float tracked = now - _trackSince;
        float spread = SvmMath.Lerp(Difficulty.AimErrorDegrees, Difficulty.AimSettledDegrees,
            SvmMath.Clamp(tracked / SvmMath.Max(0.05f, Difficulty.AimSettleSeconds), 0f, 1f));
        if (now - _errorAt > 0.25f)
        {
            _errorAt = now;
            _errorYaw = (Bot.Random.FRand() + Bot.Random.FRand() - 1f) * spread;
            _errorPitch = (Bot.Random.FRand() + Bot.Random.FRand() - 1f) * spread * 0.6f;
        }

        FVector aimPoint = c.Pawn.Location - new FVector(0f, 0f, c.Pawn.bIsCrouched ? 5f : 20f) +
            c.Pawn.Velocity * (Difficulty.ReactionTime * 0.5f * Difficulty.LeadFactor);
        FRotator aim = (aimPoint - AiSight.Eye(Pawn)).Rotation();
        const float unitsPerDegree = 65536f / 360f;
        plan.Yaw = (aim.Yaw + (int)(_errorYaw * unitsPerDegree)) & 0xFFFF;
        plan.Pitch = aim.Pitch + (int)(_errorPitch * unitsPerDegree);
        plan.Tolerance = 65536;

        // Movement: the better mercs side-step between bursts at range.
        if (Difficulty.Skill >= AiSkill.Hard && (c.Pawn.Location - Pawn.Location).Size() > 600f)
        {
            if (now - _strafeAt > 0.8f)
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

        bool reacted = c.VisibleSince >= 0f && now - c.VisibleSince >= Difficulty.ReactionTime;
        if (reacted && c.Pawn is SBasePawn spy && Melee(now, spy, ref plan))
        {
            _burstStart = -1f;
            return;
        }

        float error = AimErrorTo(c.Pawn.Location);
        bool onTarget = error < spread + 3f;
        if (!reacted || !onTarget || TeammateInTheWay(c.Pawn.Location) || Pawn.MyWeapon is not SWeaponDefense w || w.ClipAmmo <= 0)
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

        if (now - _burstStart < AiK.BurstSeconds)
        {
            plan.Keys.bFire = 1;
            return;
        }

        _burstStart = -1f;
        _pauseUntil = now + AiK.BurstPause * (Difficulty.Skill >= AiSkill.Hard ? 0.7f : 1.2f);
    }

    /// <summary>
    /// The Use key at close quarters (melee-elbow-charge-berserk.md rule 1), pressed only when the controller would
    /// take it: a charge (forward held) needs the spy 140 to 550 uu ahead in the view and the charge cone, a clear
    /// line, the charge and spin cooldowns and no reload; Use alone needs the spy within elbow reach in front, out of
    /// his wake protection, and no team mate near (the spin and the elbow hit team mates too). Each chance is taken by
    /// the difficulty's melee use, looked at once a second; the better mercs never charge or strike a spy holding
    /// his shocker on them (rule 19: the merc would shock himself). True when Use is pressed this tick.
    /// </summary>
    private bool Melee(float now, SBasePawn spy, ref AiPlan plan)
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
            World.LineOpen(AiSight.Eye(merc), spy.Location);
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

        // Use needs a fresh press (the edge of BT_UseJusteAppuye): released for a moment between two.
        if (!_meleeWanted || now - _useAt < 0.25f)
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
        return (gri is null || gri.bAvlb_MERC_Charge) && now - Pc.fLastBerserkTime > Pc.fSecondBetweenChargeAndBerserk &&
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
        if (now - _investigateArrived > 4f)
        {
            // Nothing here: the contact fades faster.
            AiContact? best = Bot.Memory.Best();
            if (best is not null && best.Confidence > 0f && !best.Visible)
            {
                best.Confidence *= 0.3f;
            }
        }
    }

    private void Guard(float now, ref AiPlan plan)
    {
        if (_post is null || World.IsTaken(_post) || now > _postUntil)
        {
            PickPost(now);
        }

        if (_post is null)
        {
            LookRound(now, ref plan, Pawn.Location + Pawn.Rotation.Vector() * 500f);
            return;
        }

        if ((Feet - _postFloor).Size() > 60f)
        {
            if (!MoveTo(_postFloor, now, ref plan, sneak: false))
            {
                _postUntil = now;
            }

            return;
        }

        LookRound(now, ref plan, _post.Location);
    }

    private void PickPost(float now)
    {
        World.Teams[1].Release(Bot);
        _post = null;
        SComputerObjectiveTrigger? best = null;
        float bestScore = float.MaxValue;
        foreach (SComputerObjectiveTrigger t in World.Terminals)
        {
            if (World.IsTaken(t))
            {
                continue;
            }

            float score = (t.Location - Pawn.Location).Size() * (0.5f + Bot.Random.FRand());
            if (World.Teams[1].IsClaimedByOther(t, Bot))
            {
                score += 5000f * Difficulty.Teamwork;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        if (best is null || !UseSpot(best, out FVector spot, out _))
        {
            _postUntil = now + 5f;
            return;
        }

        // A post beside the use spot (150 to 300 uu out), not on it.
        FVector post = spot;
        float postScore = float.MaxValue;
        foreach (int n in NodesAround(spot, 300f))
        {
            FVector f = Follower.Graph.Node(n).Floor;
            float d = NavFollower.Horizontal(f - spot);
            if (d < 150f || MathF.Abs(f.Z - spot.Z) > 40f || !Follower.Graph.Node(n).Standing)
            {
                continue;
            }

            float s = MathF.Abs(d - 220f) + Bot.Random.FRand() * 60f;
            if (s < postScore)
            {
                postScore = s;
                post = f;
            }
        }

        _post = best;
        _postFloor = post;
        _postUntil = now + 12f + Bot.Random.FRand() * 18f;
        _guardTorch = Bot.Random.FRand() < Difficulty.GadgetUse;
        _guardMotion = !_guardTorch && Bot.Random.FRand() < Difficulty.GadgetUse;
        World.Teams[1].Claim(best, Bot);
    }

    /// <summary>Looks round a point of interest: a new direction every 1.5 to 3 s, biased toward <paramref name="focus"/>.</summary>
    private void LookRound(float now, ref AiPlan plan, FVector focus)
    {
        if (now >= _lookAt)
        {
            _lookAt = now + 1.5f + Bot.Random.FRand() * 1.5f;
            int toward = NavFollower.YawTo(Pawn.Location, focus);
            int spreadUnits = (int)(Bot.Random.RandRange(-1f, 1f) * 20000f);
            _lookYaw = Bot.Random.FRand() < 0.5f ? (toward + 32768 + spreadUnits) & 0xFFFF : (toward + spreadUnits / 2) & 0xFFFF;
        }

        plan.Yaw = _lookYaw;
        plan.Pitch = 0;
        plan.Tolerance = 65536;
    }

    private void Frag(float now, ref AiPlan plan)
    {
        FVector eye = AiSight.Eye(Pawn);
        FVector to = _fragTarget - eye;
        float flat = NavFollower.Horizontal(to);

        // A full-strength throw (a tap of the throw key: 1800 uu/s) on a low arc; the pitch from the range.
        // A frag's velocity takes half of the 1500 gravity in flight (AActor::physFalling, grenade-throw.md rule 4a).
        const float speed = 1800f;
        const float gravity = 750f;
        float s = SvmMath.Clamp(gravity * flat / (speed * speed), 0f, 1f);
        float pitchRad = 0.5f * SvmMath.Asin(s) + SvmMath.Atan2(to.Z, SvmMath.Max(1f, flat)) * 0.5f;
        plan.Yaw = NavFollower.YawTo(eye, _fragTarget);
        plan.Pitch = (int)(pitchRad * (32768f / MathF.PI));
        plan.Tolerance = 65536;
        switch (_fragPhase)
        {
            case 0:
                int slot = GadgetSlot<SGadFragGrenade>(out _);
                if (slot < 0)
                {
                    return;
                }

                if (Pawn.SelectedGadget is not SGadFragGrenade)
                {
                    plan.Keys.Press(AiCommand.SelectGadget, slot);
                }

                _fragPhase = 1;
                _fragPhaseAt = now;
                return;
            case 1:
                if (now - _fragPhaseAt < 0.3f || AimErrorTo(_fragTarget) > 25f)
                {
                    if (now - _fragPhaseAt > 2f)
                    {
                        _fragPhase = 4;
                        _fragAt = now;
                    }

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

                FragsThrown++;
                _fragAt = now;
                _fragPhase = 3;
                _fragPhaseAt = now;
                return;
            default:
                if (_fragPhase == 3 && now - _fragPhaseAt > 1.8f)
                {
                    _fragPhase = 4;
                }

                return;
        }
    }

    /// <summary>The torch in the dark and motion tracking while searching, by the difficulty's gadget use.</summary>
    private void Lights(float now, ref AiPlan plan)
    {
        if (Difficulty.GadgetUse < 0.15f)
        {
            return;
        }

        bool searching = Goal is GInvestigate or GRespond;
        int mode = AiSight.RendMapOf(Pc);
        if (now - _visionAt > 4f && Goal != GEngage)
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
