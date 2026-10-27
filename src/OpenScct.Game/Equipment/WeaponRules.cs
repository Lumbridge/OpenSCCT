using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The arithmetic of the weapons and gadgets, free of actors: the rifle's distance damage and spread, the burst
/// decay, the recoil, the shot count of a rate-of-fire mode, the grenade throw strength and the spy energy. Float32
/// throughout, one operation at a time, <c>int()</c> truncating toward zero.
/// </summary>
public static class WeaponRules
{
    /// <summary>
    /// The distance factor of a merc gun (merc-rifle.md rule 11, hit-zones-and-weapons.md rule 7.10): flat CMax ramp
    /// to CoefCaC before DistCac, then linear to CoefMed at DistMed and CoefMin at DistLoin, CoefMin beyond.
    /// </summary>
    public static float DistanceFactor(float d, float CMax, float CoefCaC, float CoefMed, float CoefMin, float DistCac,
        float DistMed, float DistLoin)
    {
        if (d < DistCac)
        {
            float slope = (CoefCaC - CMax) / DistCac;
            return slope * d + CMax;
        }

        if (d < DistMed)
        {
            float slope = (CoefMed - CoefCaC) / (DistMed - DistCac);
            return slope * (d - DistCac) + CoefCaC;
        }

        if (d < DistLoin)
        {
            float slope = (CoefMin - CoefMed) / (DistLoin - DistMed);
            return slope * (d - DistMed) + CoefMed;
        }

        return CoefMin;
    }

    /// <summary><c>int(BaseDamage * factor)</c> for a hit at distance <paramref name="d"/>.</summary>
    public static int DistanceDamage(SWeaponDefense Weapon, float d)
    {
        float factor = DistanceFactor(d, Weapon.CMax, Weapon.CoefCaC, Weapon.CoefMed, Weapon.CoefMin, Weapon.DistCac,
            Weapon.DistMed, Weapon.DistLoin);
        return SvmMath.Trunc(Weapon.BaseDamage * factor);
    }

    /// <summary>
    /// GetAccuracy (native 1286): <c>min(|V| / 600 + MinDispersion + 0.1 * BulletInARow, MaxDispersion)</c>.
    /// </summary>
    [UcNative("ASWeaponDefense::execGetAccuracy", Exe = 0x10abcfc0)]
    [PortOf("nat:SBase.SWeaponDefense.GetAccuracy", Spec = "spec/equipment/merc-rifle.md#rules")]
    public static float Accuracy(float Speed, float MinDispersion, float MaxDispersion, int BulletInARow)
    {
        float a = Speed / K.AccuracySpeedDivisor + MinDispersion + K.AccuracyBurstTerm * BulletInARow;
        return SvmMath.Min(a, MaxDispersion);
    }

    /// <summary>GetNbBulletsFromROF: Single 1, Burst min(3, clip), Auto the whole clip.</summary>
    public static int BulletsForMode(SRateOfFireMode Mode, int ClipAmmo) => Mode switch
    {
        SRateOfFireMode.ROF_Single => 1,
        SRateOfFireMode.ROF_Burst => SvmMath.Min(K.BurstSize, ClipAmmo),
        _ => ClipAmmo,
    };

    /// <summary>The ROF button cycle: Auto and Single go to Burst, Burst to Auto (Single only through the scope).</summary>
    public static SRateOfFireMode NextMode(SRateOfFireMode Mode) =>
        Mode == SRateOfFireMode.ROF_Burst ? SRateOfFireMode.ROF_Auto : SRateOfFireMode.ROF_Burst;

    /// <summary>The ROF names of the HUD.</summary>
    public static string ModeName(SRateOfFireMode Mode) => Mode switch
    {
        SRateOfFireMode.ROF_Single => "Single Shot",
        SRateOfFireMode.ROF_Burst => "Burst Mode",
        _ => "Full Auto",
    };

    /// <summary>
    /// One recoil pitch kick: <c>int((ValeurReculVer + 5 r) * 33)</c> added, then clamped to +-13000 (merc-rifle.md
    /// rule 7).
    /// </summary>
    public static int KickPitch(int Pitch, float ValeurReculVer, float r)
    {
        int p = Pitch + SvmMath.Trunc((ValeurReculVer + K.RecoilRandom * r) * K.RecoilPitchFactor);
        return SvmMath.Trunc(SvmMath.FClamp(p, -K.RecoilPitchClamp, K.RecoilPitchClamp));
    }

    /// <summary>The camera yaw kick: <c>int((2 r2 - 1) * (ValeurReculHor + 5 r3) * 50)</c>.</summary>
    public static int CameraYawKick(float ValeurReculHor, float r2, float r3) =>
        SvmMath.Trunc((2f * r2 - 1f) * (ValeurReculHor + K.RecoilRandom * r3) * K.RecoilCameraYawFactor);

    /// <summary>The aim yaw kick: <c>int((ValeurReculHor + 5 r5) * 33 * (2 r6 - 1))</c>.</summary>
    public static int AimYawKick(float ValeurReculHor, float r5, float r6) =>
        SvmMath.Trunc((ValeurReculHor + K.RecoilRandom * r5) * K.RecoilAimYawFactor * (2f * r6 - 1f));

    /// <summary>
    /// The aimed throw value (grenade-throw.md rule 2): full strength within the tap window, else 0 holstered or
    /// <c>min((e - tap) / ramp, 1)</c>.
    /// </summary>
    public static float ThrowValue(float Elapsed, bool bWeaponOut, float TapWindow, float Ramp)
    {
        if (Elapsed < TapWindow)
        {
            return 1f;
        }

        if (!bWeaponOut)
        {
            return 0f;
        }

        return SvmMath.Min((Elapsed - TapWindow) / Ramp, 1f);
    }

    /// <summary>The throw force: <c>int((v + 1)^2 * ProjectileSpeed * 0.5)</c>.</summary>
    public static int ThrowForce(float v, float ProjectileSpeed)
    {
        float s = v + 1f;
        return SvmMath.Trunc(s * s * ProjectileSpeed * K.ThrowForceScale);
    }
}
