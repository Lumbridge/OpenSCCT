using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Combat;

/// <summary>
/// The arithmetic of the damage pipeline, kept free of actors so it can be tested on its own. Every expression is
/// evaluated in float32 one operation at a time, and <c>int()</c> truncates toward zero, as in the original.
/// </summary>
public static class CombatRules
{
    /// <summary>
    /// GetApproxPillFromHit: the zone of a hit point from its height on the collision cylinder and its side of the
    /// pawn's right axis (head above 0.68, chest down to the centre, legs below).
    /// </summary>
    [Uc("SBase.SBasePawn.GetApproxPillFromHit", Sha1 = "f7cd26c0feaecdc785ee95404ca133bb070c7ac2")]
    [PortOf("uc:SBase.SBasePawn:GetApproxPillFromHit", Spec = "spec/combat/hit-zones-and-weapons.md#rules")]
    public static EDamageLocation ApproxPill(FVector Location, FRotator Rotation, float CollisionHeight, FVector HitLocation)
    {
        Rotation.GetAxes(out _, out FVector y, out _);
        FVector d = HitLocation - Location;
        d.Z = 0f;
        d = d.SafeNormal();
        float dotY = d | y;
        float p = (HitLocation.Z - Location.Z) / CollisionHeight;
        if (p > K.ApproxHeadThreshold)
        {
            return EDamageLocation.P_Head;
        }

        if (p >= 0f)
        {
            return dotY < 0f ? EDamageLocation.P_ChestL : EDamageLocation.P_ChestR;
        }

        return dotY < 0f ? EDamageLocation.P_LegL : EDamageLocation.P_LegR;
    }

    /// <summary>A real pill of the 15-pill scheme (branch B of the damage pipeline).</summary>
    public static bool IsRealPill(int PillTag) => PillTag >= K.FirstPill && PillTag <= K.LastPill;

    /// <summary><c>int(Damage * Table[PillTag - 1] * 0.01)</c> (rule 20).</summary>
    public static int ZoneScaled(int Damage, float TablePercent)
    {
        float product = Damage * TablePercent;
        return SvmMath.Trunc(product * K.ZoneScale);
    }

    /// <summary>The taser damage: <c>int(default.Health * 0.51)</c> = 51.</summary>
    public static int TaserDamage(int DefaultHealth) => SvmMath.Trunc(DefaultHealth * K.TaserHealthFactor);

    /// <summary>The spy refill points: <c>int(float(ActualDamage * Percent) * 0.01)</c>, the integer product first.</summary>
    public static int RefillPoints(int ActualDamage, int PercentDamageRefillable)
    {
        int product = ActualDamage * PercentDamageRefillable;
        return SvmMath.Trunc(product * K.ZoneScale);
    }

    /// <summary>The fall damage formula: <c>(h - Min) / (Max - Min) * default.Health</c> (blast-and-fall-damage.md rule 15).</summary>
    public static float FallDamage(float Height, float MinHeight, float MaxHeight, int DefaultHealth)
    {
        float over = Height - MinHeight;
        float span = MaxHeight - MinHeight;
        return over / span * DefaultHealth;
    }

    /// <summary>
    /// The global PlayAnimDmg choice (rule 39): front and back by <c>Normal(M) . facing</c> beyond 0.6, otherwise the
    /// side of <c>facing x Z</c>.
    /// </summary>
    public static int GlobalHitAction(FRotator PawnRotation, FVector Momentum)
    {
        FVector m = Momentum.SafeNormal();
        FVector facing = PawnRotation.Vector();
        float a = m | facing;
        if (a > K.HitAnimFrontBack)
        {
            return SBasePawn.SA_DMG_SPECIAL_FRONT;
        }

        if (a < -K.HitAnimFrontBack)
        {
            return SBasePawn.SA_DMG_SPECIAL_BACK;
        }

        FVector side = facing ^ new FVector(0f, 0f, 1f);
        return (m | side) > 0f ? SBasePawn.SA_DMG_SPECIAL_LEFT : SBasePawn.SA_DMG_SPECIAL_RIGHT;
    }

    /// <summary>
    /// The standing PlayAnimDmg choice by pill (rule 40), with the original's asymmetric sign tests for pills 2 and 9;
    /// 0 plays nothing.
    /// </summary>
    public static int StandingHitAction(int PillTag, FRotator PawnRotation, FVector Momentum)
    {
        float a = Momentum.SafeNormal() | PawnRotation.Vector();
        switch (PillTag)
        {
            case 1:
                return SBasePawn.SA_DMG_HEAD;
            case 2:
                return SvmMath.Abs(a) < K.HitAnimFrontBack ? SBasePawn.SA_DMG_CHEST_LEFT
                    : a > 0f ? SBasePawn.SA_DMG_CHEST_BACK : SBasePawn.SA_DMG_CHEST_FRONT;
            case 9:
                return SvmMath.Abs(a) < K.HitAnimFrontBack ? SBasePawn.SA_DMG_CHEST_RIGHT
                    : a < 0f ? SBasePawn.SA_DMG_CHEST_BACK : SBasePawn.SA_DMG_CHEST_FRONT;
            case 6:
            case 7:
            case 8:
                return SBasePawn.SA_DMG_LEG_RIGHT;
            case 13:
            case 14:
            case 15:
                return SBasePawn.SA_DMG_LEG_LEFT;
            case 3:
            case 4:
            case 5:
                return SBasePawn.SA_DMG_ARM_LEFT;
            case 10:
            case 11:
            case 12:
                return SBasePawn.SA_DMG_ARM_RIGHT;
            default:
                return SBasePawn.SA_NONE;
        }
    }

    /// <summary>The death action of PlayAnimDeath (rule 45): falling for (0,0,1), else by direction; 0 plays nothing.</summary>
    public static int DeathAction(EDeathDirection Direction) => Direction switch
    {
        EDeathDirection.DIE_Forward => SBasePawn.SA_DIE_FORWARD,
        EDeathDirection.DIE_Backward => SBasePawn.SA_DIE_BACKWARD,
        EDeathDirection.DIE_Left => SBasePawn.SA_DIE_LEFT,
        EDeathDirection.DIE_Right => SBasePawn.SA_DIE_RIGHT,
        EDeathDirection.DIE_InPlace => SBasePawn.SA_DIE_INPLACE,
        _ => SBasePawn.SA_NONE,
    };

    /// <summary>
    /// SBasePawn.PlayFirstPersonDamage: the merc weapon pain clip index for a hit action (rule 43), or -1 for an
    /// action without one.
    /// </summary>
    [Uc("SBase.SBasePawn.PlayFirstPersonDamage", Sha1 = "70740280ecd283fd82104e1deca3f8bcd2689063")]
    [PortOf("uc:SBase.SBasePawn:PlayFirstPersonDamage", Spec = "spec/combat/damage-pipeline.md#rules")]
    public static int FirstPersonPain(int Action) => Action switch
    {
        SBasePawn.SA_DMG_SPECIAL_LEFT or SBasePawn.SA_DMG_CHEST_LEFT or SBasePawn.SA_DMG_ARM_LEFT or SBasePawn.SA_DMG_LEG_LEFT => 2,
        SBasePawn.SA_DMG_SPECIAL_RIGHT or SBasePawn.SA_DMG_CHEST_RIGHT or SBasePawn.SA_DMG_ARM_RIGHT or SBasePawn.SA_DMG_LEG_RIGHT => 3,
        SBasePawn.SA_DMG_SPECIAL_FRONT or SBasePawn.SA_DMG_CHEST_FRONT => 1,
        SBasePawn.SA_DMG_SPECIAL_BACK or SBasePawn.SA_DMG_CHEST_BACK => 0,
        _ => -1,
    };

    /// <summary>
    /// The freeze and electric times of a shock (shock-and-taser.md rule 2): the freeze times the rain factor when
    /// wet, then the pill's extra electric time (an out-of-range pill reads the clamped first or last element).
    /// </summary>
    public static void ShockTimes(SBasePawn Pawn, int PillTag, float RainFreezeFactor, out float Freeze, out float Shock)
    {
        Freeze = Pawn.Shock_FreezeDuration;
        if (Pawn.bWet)
        {
            Freeze *= RainFreezeFactor;
        }

        int index = SvmMath.Clamp(PillTag - 1, 0, Pawn.Shock_Duration.Length - 1);
        Shock = Freeze + Pawn.Shock_Duration[index];
    }

    /// <summary>
    /// Handle_ElbowDmg's count: history entries of type SDamage_ElbowShock with <c>now - Time &lt; window</c> (Time is in
    /// whole seconds).
    /// </summary>
    public static int RecentElbowHits(FDamageInfos[] LastDamages, float Now, int WindowSeconds)
    {
        int count = 0;
        foreach (FDamageInfos d in LastDamages)
        {
            if (d.DamageType == SDamageType.SDamage_ElbowShock && Now - d.Time < WindowSeconds)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The blast speed and direction of ExplosionHit for a momentum shorter than BlastDistMax (rule 1).</summary>
    public static FVector BlastVelocity(FVector Momentum, float BlastDistMax, float BlastSpeedMax, float BlastSpeedMin)
    {
        float d = Momentum.Size();
        float s = BlastSpeedMax + (BlastSpeedMin - BlastSpeedMax) * d / BlastDistMax;
        FVector dir = d > 0f ? Momentum / d : FVector.Zero;
        dir.Z = 1f;
        return dir.SafeNormal() * s;
    }
}
