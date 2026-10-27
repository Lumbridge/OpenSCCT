using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// The look of a sticky shock on the pawn (spec/equipment/sticky-shock-effect.md rule 3.6): seven spark emitters on
/// the limbs, the head, the pelvis and the spine (the first with a blinking blue light) and the Stickess skin, both
/// started by the pawn's SA_DMG_ELECTRIC action and removed by SA_DMG_ELECTRIC_STOP on every machine that runs the
/// action (ExecuteAction, spec/native/execute-action.md actions 53 to 55 and the jammer's 271 and 272).
/// </summary>
public partial class SBasePawn
{
    /// <summary>The bones the seven sparks hang from, in Shock_SfxActors order.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.StickyShockSfx: B R ForeArm, B L ForeArm, B R Calf, B L Calf, B Pelvis, B Head, B Spine2")]
    public static readonly Name[] ShockSfxBones =
    [
        new("B R ForeArm"), new("B L ForeArm"), new("B R Calf"), new("B L Calf"), new("B Pelvis"), new("B Head"), new("B Spine2"),
    ];

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.StickyShockSfx: the first spark's light, LT_Blink hue 145 saturation 174 brightness 150 radius 10")]
    public const byte ShockLightHue = 145;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.StickyShockSfx")]
    public const byte ShockLightSaturation = 174;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.StickyShockSfx")]
    public const float ShockLightBrightness = 150f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.StickyShockSfx")]
    public const float ShockLightRadius = 10f;

    /// <summary>The native-callable wrapper of <see cref="StickyShockSfx"/>.</summary>
    [Uc("SBase.SBasePawn.evStickyShockSfx", Sha1 = "e8d2a7380de36373fd0e406df7e9b02ad03fc5c3")]
    [PortOf("uc:SBase.SBasePawn:evStickyShockSfx", Spec = "spec/equipment/sticky-shock-effect.md#rules")]
    public void evStickyShockSfx(bool bSpawn) => StickyShockSfx(bSpawn);

    /// <summary>
    /// Spawns the seven SEffectShockedPawnEmitter sparks at their bones (any missing one) or destroys them all; not on
    /// a dedicated server's own copy.
    /// </summary>
    [Uc("SBase.SBasePawn.StickyShockSfx", Sha1 = "6fd61e5040e22f82312d2ec9650169633dbc2c3f")]
    [PortOf("uc:SBase.SBasePawn:StickyShockSfx", Spec = "spec/equipment/sticky-shock-effect.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The spawn point is the bone's placement as CombatPorts gives it (the pawn's own, without a skeleton); the view draws each spark at its drawn bone")]
    public void StickyShockSfx(bool bSpawn)
    {
        if (Level.NetMode == ENetMode.NM_DedicatedServer && Role == ENetRole.ROLE_Authority)
        {
            return;
        }

        if (!bSpawn)
        {
            for (int i = 0; i < Shock_SfxActors.Length; i++)
            {
                Shock_SfxActors[i]?.Destroy();
                Shock_SfxActors[i] = null;
            }

            return;
        }

        for (int i = 0; i < Shock_SfxActors.Length; i++)
        {
            if (Shock_SfxActors[i] is not null)
            {
                continue;
            }

            Name bone = ShockSfxBones[i];
            FCoords at = CombatServices.Ports(this).BoneCoords(this, bone);
            // The script turns the second spark like the right forearm, the others like their own bone.
            FRotator rot = (i == 1 ? CombatServices.Ports(this).BoneCoords(this, ShockSfxBones[0]) : at).XAxis.Rotation();
            var spark = XLevel.SpawnActor(new SEffectShockedPawnEmitter(), at.Origin, rot, this, null);
            if (spark is null)
            {
                continue;
            }

            ObjectiveNatives.AttachToBone(this, spark, bone);
            if (i == 0)
            {
                spark.bDynamicLight = true;
                spark.LightType = ELightType.LT_Blink;
                spark.LightHue = ShockLightHue;
                spark.LightSaturation = ShockLightSaturation;
                spark.LightBrightness = ShockLightBrightness;
                spark.LightRadius = ShockLightRadius;
                spark.bLightChanged = true;
            }

            Shock_SfxActors[i] = spark;
        }
    }

    /// <summary>The Stickess skin while shocked (SPersoTextures.alpha_ATT02 / alpha_DEF01 Stickess shaders).</summary>
    [Uc("SBase.SBasePawn.BeginStickySkin", Sha1 = "da689356cc8f7d7e24a9ebae98670aa6597d3681")]
    [PortOf("uc:SBase.SBasePawn:BeginStickySkin", Spec = "spec/equipment/sticky-shock-effect.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The Skins array is not modelled: the flag tells the pawn view to draw the Stickess shaders")]
    public void BeginStickySkin() => bStickySkinned = true;

    /// <summary>Back to the pawn's own skin (the wet and phospho skins are chosen by the view from bWet and PhosphoSkinTime).</summary>
    [Uc("SBase.SBasePawn.RestoreSkins", Sha1 = "5a7e2a9dc9224ca5320ea643b1b1f7dc0b82162e")]
    [PortOf("uc:SBase.SBasePawn:RestoreSkins", Spec = "spec/equipment/sticky-shock-effect.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The Skins array is not modelled: only the Stickess flag is cleared")]
    public void RestoreSkins() => bStickySkinned = false;
}
