using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.AI;

/// <summary>What a spy bot's evasion did on one tick (diagnostics and tests).</summary>
public enum SpyEvasionAct : byte
{
    None,

    /// <summary>Kept the walk under the tracker's speed: crouched at the slow speed (88.5 uu/s unarmed, 61.5 armed).</summary>
    Creep,

    /// <summary>Stood still: a merc that could be tracking was looking its way.</summary>
    Freeze,
}

/// <summary>
/// A spy bot playing around the merc's motion tracker (spec/equipment/motion-tracking.md): the tracker locks a pawn
/// that moves at 150 uu/s or more within 2600 uu of a merc (plus the pawn's radius) with an open line, and holds the
/// lock for about two seconds after it stops. A player who knows the rule creeps (crouched at the slow speed, 88.5
/// uu/s, or 61.5 with the gun drawn; standing at the slow speed is 169 and is locked), keeps still while a merc looks
/// its way in range, moves while a wall is between them, and draws the gun with the long press (no laser for the merc
/// to see). The bot does the same from what it knows of the mercs (its memory: seen, heard or told positions), never
/// from hidden state: it does not know a merc's vision mode, so it assumes the tracker may be on. How carefully it
/// plays is the difficulty's stealth.
/// </summary>
public sealed class SpyEvasion
{
    private readonly AiController _bot;
    private float _holdDrawUntil = -1f;
    private bool _holding;

    public SpyEvasion(AiController bot)
    {
        _bot = bot ?? throw new ArgumentNullException(nameof(bot));
    }

    /// <summary>What the last tick did.</summary>
    public SpyEvasionAct Last { get; private set; }

    /// <summary>Ticks that crept or froze (diagnostics).</summary>
    public int CreepTicks { get; private set; }

    public int FreezeTicks { get; private set; }

    /// <summary>Draws made with the long press.</summary>
    public int HoldDraws { get; private set; }

    /// <summary>The tracker's range as the bot plays it: 2600 + the spy's radius, plus a margin by difficulty.</summary>
    [Provenance(ProvenanceKind.Decoded, "SMotionTrackingVision MaxDetectedDistance 2600 + CollisionRadius (GetActors)")]
    public static float TrackedRange(SBasePawn spy, AiDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(spy);
        ArgumentNullException.ThrowIfNull(difficulty);
        return 2600f + spy.CollisionRadius + EvasionK.RangeMargin * difficulty.Stealth;
    }

    /// <summary>
    /// The mercs the bot believes could track it now: a contact sure enough for its difficulty, within the tracked
    /// range, with an open line from the contact's position to the spy. <paramref name="looking"/> is set when one of
    /// them is in sight now, near, and faces the spy.
    /// </summary>
    public bool Threatened(SBasePawn spy, out bool looking)
    {
        ArgumentNullException.ThrowIfNull(spy);
        looking = false;
        AiDifficulty d = _bot.Difficulty;
        float range = TrackedRange(spy, d);
        float sure = EvasionK.MinConfidence + (1f - d.Stealth) * 0.4f;
        bool threat = false;
        foreach (AiContact c in _bot.Memory.Contacts)
        {
            if (c.Pawn is not SPAWNDEFENSE || c.Confidence < sure && !c.Visible)
            {
                continue;
            }

            FVector at = c.Visible ? c.Pawn.Location : c.Position;
            float dist = (at - spy.Location).Size();
            if (dist > range || !_bot.World.LineOpen(at, spy.Location))
            {
                continue;
            }

            threat = true;
            if (c.Visible && dist < EvasionK.FreezeRange * d.Stealth + 400f)
            {
                FVector toSpy = spy.Location - c.Pawn.Location;
                float len = toSpy.Size();
                FRotator view = c.Pawn.Rotation;
                if (len < 1f || (toSpy | view.Vector()) / len > EvasionK.FacingCos)
                {
                    looking = true;
                }
            }
        }

        return threat;
    }

    /// <summary>
    /// Adjusts the brain's plan: under threat a walk becomes a crouched creep at the slow speed, and with a merc
    /// looking on (Hard and Expert) the bot keeps still; a draw of the gun near mercs uses the long press.
    /// </summary>
    public void Apply(ref AiPlan plan, float now)
    {
        Last = SpyEvasionAct.None;
        if (_bot.Pawn is not SBasePawn spy || _bot.Difficulty.Stealth < EvasionK.MinStealth)
        {
            _holding = false;
            return;
        }

        bool threat = Threatened(spy, out bool looking);
        HoldDraw(ref plan, spy, threat, now);
        if (!threat || !plan.Keys.Moving || plan.Keys.Command1 == AiCommand.Jump || plan.Keys.Command2 == AiCommand.Jump ||
            plan.Crouched == false || spy.Physics != EPhysics.PHYS_Walking)
        {
            return;
        }

        // Hurt just now: running beats creeping.
        if (_bot.World.Now - _bot.HurtAt < 2f)
        {
            return;
        }

        if (looking && _bot.Difficulty.Stealth >= EvasionK.FreezeStealth)
        {
            plan.Keys.BT_Up = 0;
            plan.Keys.BT_Down = 0;
            plan.Keys.BT_Left = 0;
            plan.Keys.BT_Right = 0;
            Last = SpyEvasionAct.Freeze;
            FreezeTicks++;
            return;
        }

        // In the light the merc sees the spy whatever the speed: getting through fast is the safer play.
        if (Lit(spy))
        {
            return;
        }

        plan.Keys.FastSpeed = false;
        plan.Crouched = true;
        Last = SpyEvasionAct.Creep;
        CreepTicks++;
    }

    /// <summary>
    /// True when the spy stands in the light as a remembered merc would see it (the level the bot's own light
    /// reading gives toward that merc's eye): there the tracker adds nothing to what the merc sees.
    /// </summary>
    private bool Lit(SBasePawn spy)
    {
        foreach (AiContact c in _bot.Memory.Contacts)
        {
            if (c.Pawn is not SPAWNDEFENSE)
            {
                continue;
            }

            FVector at = c.Visible ? AiSight.Eye(c.Pawn) : c.Position + new FVector(0f, 0f, c.Pawn.EyeHeight);
            if (_bot.World.LightOn(spy, at) > EvasionK.LitLevel)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The draw with the long press: a press of the fire-mode key while holstered is held for more than half a second
    /// (no laser); the holster press is left alone.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spy-shocker.md rule 1: a 0.5 s hold draws without the laser")]
    private void HoldDraw(ref AiPlan plan, SBasePawn spy, bool threat, float now)
    {
        if (_holding)
        {
            if (now < _holdDrawUntil && spy.WeaponLoaded == 0)
            {
                plan.Keys.bFireMode = 1;
                return;
            }

            _holding = false;
            plan.Keys.bFireMode = 0;
            return;
        }

        if (plan.Keys.bFireMode != 0 && spy.WeaponLoaded == 0 && (threat || _bot.Difficulty.Stealth >= EvasionK.FreezeStealth))
        {
            _holding = true;
            _holdDrawUntil = now + EvasionK.HoldDrawSeconds;
            HoldDraws++;
        }
    }
}

/// <summary>Constants of the spy bots' evasion (play choices, not game values).</summary>
public static class EvasionK
{
    /// <summary>Extra range (uu) a fully stealthy bot keeps from a merc's tracker on top of 2600 + its radius.</summary>
    [Provenance(ProvenanceKind.Estimated, "Bot play choice: a margin for the merc's own movement between two scans")]
    public const float RangeMargin = 150f;

    /// <summary>Lowest confidence of a remembered merc that counts (plus up to 0.4 for careless bots).</summary>
    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float MinConfidence = 0.25f;

    /// <summary>Stealth below which a bot does not evade at all (the Easy preset).</summary>
    [Provenance(ProvenanceKind.Estimated, "Bot play choice: Easy (stealth 0.2) plays carelessly")]
    public const float MinStealth = 0.3f;

    /// <summary>Stealth from which a bot keeps still under a merc's eyes and draws with the long press (Hard, Expert).</summary>
    [Provenance(ProvenanceKind.Estimated, "Bot play choice: Hard (0.85) and Expert (1.0)")]
    public const float FreezeStealth = 0.8f;

    /// <summary>Distance (uu, times stealth, plus 400) within which a merc looking on makes the bot keep still.</summary>
    [Provenance(ProvenanceKind.Estimated, "Bot play choice")]
    public const float FreezeRange = 1600f;

    /// <summary>Cosine of the angle from a merc's view within which it counts as looking at the spy (about 50 degrees).</summary>
    [Provenance(ProvenanceKind.Estimated, "Bot play choice: about the merc's half FOV")]
    public const float FacingCos = 0.64f;

    /// <summary>Light on the spy above which a bot does not creep (the level the spy bot's hide choice calls dark).</summary>
    [Provenance(ProvenanceKind.Estimated, "Bot play choice: SpyAi hides below 0.25")]
    public const float LitLevel = 0.25f;

    /// <summary>The long press: more than the draw's 0.5 s hold.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerController fire-mode PlayerTick: fDateFireMode = now + 0.5, held past it draws without the laser")]
    public const float HoldDrawSeconds = 0.55f;
}
