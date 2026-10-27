using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The blast of the frag grenade and the mines (spec/native/natives2-damage.md rules 4 and 5): every actor of the
/// level in reach and in sight takes linear-falloff damage; pawns through their controller's PawnTakeDamage (with the
/// spy hold rule), gameplay objects through TakeDamage; pawns out of reach but within the shake range shake.
/// </summary>
public static class GadgetBlast
{
    /// <summary>Spy hold: the pair counts when within 30 uu vertically.</summary>
    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md Data: the spy hold rule, 30 uu vertically")]
    public const float HoldVertical = 30f;

    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 4: retry lines to 0.9 H above and below the pawn")]
    public const float SightRetryHeight = 0.9f;

    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 1.3.4: ClientShake(50 S / d2, 2000 (S / d2), 500, true, true, true)")]
    public const int ShakeStrength = 50;

    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 1.3.4")]
    public const int ShakeSpeed = 2000;

    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 1.3.4")]
    public const int ShakeFadeOut = 500;

    /// <summary>The frag's first sight line: 0x286 plus the sight bit.</summary>
    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 4: flags 0x20286")]
    public const ETraceFlags FragSightFlags = GadgetWorld.SightFlags | ETraceFlags.TRACE_Sight;

    /// <summary>The parameters of one blast.</summary>
    public readonly record struct Blast(
        Actor Source,
        FVector ReachFrom,
        FVector FalloffFrom,
        float Range,
        bool bIntRange,
        int Damage,
        float DamageRange,
        float DistForBlast,
        SDamageType CloseType,
        SDamageType FarType,
        SDamageType ObjectType,
        int PercentDamageRefillable,
        int ShakeRange,
        Pawn? Instigator,
        bool bMine,
        Pawn? Detonator = null);

    /// <summary>
    /// Runs a blast over a copy of the level's actor list (in order). Mines (<see cref="Blast.bMine"/>) only hurt
    /// gameplay objects and extinguishers among non-pawns; a frag hurts any actor.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rules 4 and 5: reach, sight lines, falloff, damage types, spy hold, shake")]
    public static void Run(in Blast b)
    {
        bool? outer = CurrentIsMine;
        Pawn? outerDetonator = CurrentDetonator;
        CurrentIsMine = b.bMine;
        CurrentDetonator = b.Detonator;
        try
        {
            RunBlast(b);
        }
        finally
        {
            CurrentIsMine = outer;
            CurrentDetonator = outerDetonator;
        }
    }

    /// <summary>
    /// While a blast runs: true for a mine, false for a frag, null otherwise. The kill count reads it to tell a
    /// grenade kill from a mine kill, since both arrive with the same damage types (OpenSCCT's own, for achievements).
    /// </summary>
    [System.ThreadStatic]
    private static bool? s_currentIsMine;

    public static bool? CurrentIsMine
    {
        get => s_currentIsMine;
        private set => s_currentIsMine = value;
    }

    /// <summary>
    /// While a mine blast runs: the spy who set the mine off (a merc mine a spy shot), else null. OpenSCCT's own, for
    /// the spy's mine kill achievements; the kill itself stays the mine owner's.
    /// </summary>
    [System.ThreadStatic]
    private static Pawn? s_currentDetonator;

    public static Pawn? CurrentDetonator
    {
        get => s_currentDetonator;
        private set => s_currentDetonator = value;
    }

    private static void RunBlast(in Blast b)
    {
        Level level = b.Source.XLevel;
        var actors = new List<Actor>();
        foreach (Actor a in level.AllActors<Actor>())
        {
            // The frag's copy of the list leaves out hidden actors (0x10ab4a8d: bHidden at +0x2e8); the mines walk the
            // level's list as it is (0x10ac12e3), hidden actors included.
            if (!ReferenceEquals(a, b.Source) && !a.bDeleteMe && (b.bMine || !a.bHidden))
            {
                actors.Add(a);
            }
        }

        float range2 = b.Range * b.Range;
        float shake2 = (float)b.ShakeRange * b.ShakeRange;
        foreach (Actor a in actors)
        {
            if (a.bDeleteMe || a.XLevel is null)
            {
                continue;
            }

            float d2f = FVector.DistSquared(a.Location, b.ReachFrom);
            float d2 = b.bIntRange ? SvmMath.Trunc(d2f) : d2f;
            // A frag with Range 0 reaches everything (0x10ab4aea); a mine has no such case (0x10ac134a compares
            // against MyRange squared only).
            bool inReach = (!b.bMine && b.Range == 0f) || d2 < range2;
            if (!inReach)
            {
                if (a is SBasePawn shaken && d2 < shake2)
                {
                    Shake(shaken, SvmMath.Trunc(d2), b.ShakeRange);
                }

                continue;
            }

            if (a is SBasePawn pawn)
            {
                if (!PawnInSight(b, pawn))
                {
                    continue;
                }

                float dist = FVector.Dist(a.Location, b.FalloffFrom);
                int damage = b.DamageRange == 0f ? b.Damage : SvmMath.Trunc((1f - dist / b.DamageRange) * b.Damage);
                FVector hit = a.Location + new FVector(0f, 0f, 0.5f * a.CollisionHeight);
                SDamageType type = dist < b.DistForBlast ? b.CloseType : b.FarType;
                HurtPawn(pawn, damage, b.Instigator, hit, a.Location - b.FalloffFrom, type, b.PercentDamageRefillable);
                continue;
            }

            if (b.bMine && a is not (SBasicGamePlayObject or SExtincteur))
            {
                continue;
            }

            if (a is SMines { bExploding: true } || a is SProjFrag { bExploding: true })
            {
                continue;
            }

            if (!Clear(b.Source, b.ReachFrom, a.Location, b.bMine ? GadgetWorld.SightFlags : FragSightFlags, a))
            {
                continue;
            }

            float od = FVector.Dist(a.Location, b.FalloffFrom);
            int objectDamage = !b.bMine && b.DamageRange == 0f ? b.Damage : SvmMath.Trunc((1f - od / b.DamageRange) * b.Damage);
            FVector momentum = a.Location - b.FalloffFrom;
            if (b.bMine && a is SExtincteur)
            {
                FRotator r = a.Rotation;
                momentum = new FRotator(r.Pitch, r.Yaw + 16384, r.Roll).Vector().SafeNormal();
            }

            FVector at = b.bMine ? a.Location : a.Location + new FVector(0f, 0f, 0.5f * a.CollisionHeight);
            a.eventTakeDamage(objectDamage, b.Instigator, at, momentum, b.ObjectType);
        }
    }

    /// <summary>
    /// The pawn sight test: a line to the pawn (the frag adds the sight bit), then 0.9 H above and below it; the last
    /// line tried must hit nothing or the pawn.
    /// </summary>
    private static bool PawnInSight(in Blast b, SBasePawn pawn)
    {
        ETraceFlags first = b.bMine ? GadgetWorld.SightFlags : FragSightFlags;
        if (Clear(b.Source, b.ReachFrom, pawn.Location, first, pawn))
        {
            return true;
        }

        float h = SightRetryHeight * pawn.CollisionHeight;
        FVector up = new(pawn.Location.X, pawn.Location.Y, pawn.Location.Z + h);
        if (Clear(b.Source, b.ReachFrom, up, GadgetWorld.SightFlags, pawn))
        {
            return true;
        }

        FVector down = new(pawn.Location.X, pawn.Location.Y, pawn.Location.Z - h);
        return Clear(b.Source, b.ReachFrom, down, GadgetWorld.SightFlags, pawn);
    }

    /// <summary>A sight line that hits nothing or only the target (a world hit, with no actor, blocks).</summary>
    private static bool Clear(Actor source, FVector from, FVector to, ETraceFlags flags, Actor target) =>
        !GadgetWorld.Line(source, to, from, flags, out FCheckResult hit) || ReferenceEquals(hit.Actor, target);

    /// <summary>
    /// The blast shake of a pawn out of reach but within the shake range (natives2-damage.md rule 1.3.4, the same law
    /// in ProcessDamage, the frag and the mines): <c>ClientShake(50 S / d2, 2000 (S / d2), 500, true, true, true)</c>
    /// with <c>S = ShakeRange^2</c> and integer division. The event goes to any controller; only a player controller
    /// has ClientShake.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "ProcessDamage 0x10a90055, Explode 0x10ab4b2e, SMineProx.Explosion 0x10ac18a5: the integer shake law")]
    public static void Shake(SBasePawn Victim, int DistSquared, int ShakeRange)
    {
        if (Victim.Controller is not SPlayerController pc || DistSquared <= 0)
        {
            return;
        }

        int s = ShakeRange * ShakeRange;
        pc.ClientShake(ShakeStrength * s / DistSquared, ShakeSpeed * (s / DistSquared), ShakeFadeOut, true, true, true);
    }

    /// <summary>
    /// PawnTakeDamage with the spy hold rule: a spy holding a target within 30 uu vertically shares the blast with
    /// it; a held merc within 30 uu is skipped in its own turn. The frag and the proximity mine send to the target
    /// first (<paramref name="bTargetFirst"/>), ProcessDamage to the spy first. A shared copy reaches whichever of the
    /// two has a player controller, each tested on its own.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Explode 0x10ab4e20 and SMineProx.Explosion 0x10ac1563 (target first), ProcessDamage 0x10a8ffb4 (spy first)")]
    public static void HurtPawn(SBasePawn Victim, int Damage, Pawn? Instigator, FVector HitLocation, FVector Momentum,
        SDamageType Type, int PercentDamageRefillable, bool bTargetFirst = true)
    {
        if (Victim.SPawnType == PawnType.SPAWNDEFENSE && Victim.m_AttackedBy is { } holder &&
            SvmMath.Abs(holder.Location.Z - Victim.Location.Z) < HoldVertical)
        {
            return;
        }

        SPlayerController? own = Victim.Controller as SPlayerController;
        if (Victim.SPawnType == PawnType.SPAWNOFFENSE && Victim.m_AttackTarget is { } target &&
            SvmMath.Abs(target.Location.Z - Victim.Location.Z) < HoldVertical)
        {
            SPlayerController? held = target.Controller as SPlayerController;
            SPlayerController? first = bTargetFirst ? held : own;
            SPlayerController? second = bTargetFirst ? own : held;
            first?.PawnTakeDamage(Damage, Instigator, HitLocation, Momentum, Type, 0, PercentDamageRefillable);
            second?.PawnTakeDamage(Damage, Instigator, HitLocation, Momentum, Type, 0, PercentDamageRefillable);
            return;
        }

        own?.PawnTakeDamage(Damage, Instigator, HitLocation, Momentum, Type, 0, PercentDamageRefillable);
    }
}
