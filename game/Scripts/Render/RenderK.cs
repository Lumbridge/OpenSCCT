using Godot;
using OpenScct.Provenance;

namespace OpenScctGame.Render;

/// <summary>
/// Numbers the Faithful renderer takes from the original, each with its evidence. The rules that use them are
/// written down in docs/RENDERING.md.
/// </summary>
public static partial class RenderK
{
    // ---- baked light -----------------------------------------------------------------------------------------

    /// <summary>BSP lightmaps are stored at half intensity and modulated 2x with the texture in display space.</summary>
    [Provenance(ProvenanceKind.Decoded, "UModel lightmap texels are half intensity; D3D Modulate2X stage (docs/RENDERING.md#baked-light)")]
    public const float LightmapScale = 2f;

    /// <summary>
    /// The effective factor the UE5 client measured on MissE and FactD. Kept for calibration runs only: on the SteeE
    /// goldens the decoded <see cref="LightmapScale"/> scores best (median SSIM 0.966 at 2.0, 0.952 at 2.25, 0.928 at
    /// 2.5).
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "MissE / FactD capture comparisons, display = tex x lightmap x ~2.5")]
    public const float MeasuredLightmapScale = 2.5f;

    /// <summary>Static mesh baked vertex colours are modulated 2x with the texture in display space.</summary>
    [Provenance(ProvenanceKind.Decoded, "StaticMeshInstance colour stream drawn with Modulate2X (docs/RENDERING.md#baked-light)")]
    public const float VertexColorScale = 2f;

    /// <summary>Largest value of the 2x light term (the vertex colour saturates at 1 before the 2x stage).</summary>
    [Provenance(ProvenanceKind.Decoded, "D3D8 vertex colour saturation then Modulate2X (docs/RENDERING.md#actor-lighting)")]
    public const float LightTermMax = 2f;

    /// <summary>Cap of the AmbientGlow term added to baked vertex light (2 x glow / 255, capped).</summary>
    [Provenance(ProvenanceKind.Measured, "BankD skyline sweep of the glow term: best match at a cap of 1")]
    public const float AmbientGlowCap = 1f;

    /// <summary>
    /// Top mip levels the original leaves out of world textures at its shipped TextureDetailWorld=Normal setting
    /// (the textures read softer than the full-size images).
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "SteeE golden sweep of dropped levels against captures (tools/golden)")]
    public const int WorldMipDrop = 0;

    /// <summary>Alpha test reference of masked materials.</summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 D3D alpha ref 127 of 255; glTF alphaCutoff 0.5 in the converted maps")]
    public const float MaskedAlphaRef = 0.5f;

    // ---- actor lighting (pawns, unbaked meshes) ----------------------------------------------------------------

    /// <summary>Lights that light one actor (Actor.MaxLights default; no pawn class overrides it).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.u Actor.MaxLights default 4")]
    public const int MaxActorLights = 4;

    /// <summary>Point light gain: the light colour carries /510 and the stage modulates 2x.</summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight falloff f(d) = 2 S(d/R), editor 0x10f29790")]
    public const float PointLightGain = 2f;

    /// <summary>LE_Sunlight gain, unattenuated.</summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight sun branch 1.75 x C")]
    public const float SunLightGain = 1.75f;

    /// <summary>FGetHSV brightness at V 255: 0.7 x 1.4 / (sqrt(1.4) + 0.01).</summary>
    [Provenance(ProvenanceKind.Decoded, "FGetHSV, editor 0x11096ab0")]
    public const float HsvFullBrightness = 0.8213f;

    /// <summary>World light radius in uu per LightRadius unit: R = 25 x (LightRadius + 1).</summary>
    [Provenance(ProvenanceKind.Decoded, "AActor::WorldLightRadius, editor 0x10f57a20")]
    public const float LightRadiusUnits = 25f;

    /// <summary>Spot cone: Phi = 1.15 acos((1 - LightCone / 256)^2), Theta = 0.05 Phi.</summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight spot branch")]
    public const float SpotPhiScale = 1.15f;

    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight spot branch")]
    public const float SpotThetaRatio = 0.05f;

    /// <summary>LightEffect value of LE_Sunlight in SCCT's ELightEffect.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 19 LE_Sunlight (SCCT has no LE_Negative)")]
    public const int LE_Sunlight = 19;

    /// <summary>LightEffect value of LE_ESpotShadow (runtime window and sun projectors).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 22 LE_ESpotShadow")]
    public const int LE_ESpotShadow = 22;

    /// <summary>LightEffect value of LE_PointAmbient (the flash lights and muzzle flashes).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 21 LE_PointAmbient")]
    public const int LE_PointAmbient = 21;

    /// <summary>LightEffect values of the spot shapes (LE_StaticSpot, LE_Spotlight).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect")]
    public const int LE_StaticSpot = 8;

    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect")]
    public const int LE_Spotlight = 12;

    // ---- post (glow gate, vision) ----------------------------------------------------------------------------

    /// <summary>The glow composite keeps scene x saturate(16 (dot(scene, luma) - 0.03)): near-black is crushed.</summary>
    [Provenance(ProvenanceKind.Decoded, "SCCT glow composite ps_1_1, glow driver 0x10967740 (bridge docs/EFFECTS.md)")]
    public const float GlowGateGain = 16f;

    [Provenance(ProvenanceKind.Decoded, "SCCT glow composite ps_1_1")]
    public const float GlowGateOffset = 0.03f;

    [Provenance(ProvenanceKind.Decoded, "SCCT glow composite ps_1_1 luma weights")]
    public static readonly Vector3 GlowGateLuma = new(0.311f, 0.524f, 0.165f);

    /// <summary>Night vision (RendMap 34): out = saturate(4 n (L tint + glow)), L = dot(scene, luma).</summary>
    [Provenance(ProvenanceKind.Decoded, "night vision post pass ps_1_1 (bridge docs/VISION_MODES.md)")]
    public const float NightVisionGain = 4f;

    [Provenance(ProvenanceKind.Decoded, "night vision post pass ps_1_1")]
    public static readonly Vector3 NightVisionTint = new(0.718f, 1f, 0.667f);

    [Provenance(ProvenanceKind.Decoded, "night vision post pass ps_1_1 display luminance")]
    public static readonly Vector3 NightVisionLuma = new(0.3f, 0.59f, 0.11f);

    /// <summary>Spread of the multiplicative grain (Engine.Noise0_gauss, mean 0.5); the texture is not exported yet.</summary>
    [Provenance(ProvenanceKind.Estimated, "grain of about x0.64..x1.36 read from captures; sigma chosen to match")]
    public const float NightVisionNoiseSigma = 0.09f;

    /// <summary>PlayerController.RendMap codes.</summary>
    [Provenance(ProvenanceKind.Measured, "live RendMap reads: 5 normal, 34 night, 36 thermal, 37 motion, 38 EMF, 50 binoculars")]
    public const int RendMapNormal = 5;

    [Provenance(ProvenanceKind.Measured, "live RendMap reads")]
    public const int RendMapNight = 34;

    // ---- camera ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The picture aspect FovAngle is horizontal for. Wider pictures keep the vertical angle of this frame (Hor+): at
    /// 16:9 and FovAngle 75 the original shows about 91 degrees across.
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "SteeE golden captures at 1280x720 with FovAngle 75 against renders (tools/golden)")]
    public const float FovReferenceAspect = 4f / 3f;

    /// <summary>
    /// The original's pictures sit half a pixel down and right of a Vulkan render of the same camera (D3D's pixel
    /// centre convention); views shift their projection by this many pixels.
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "phase correlation of SteeE golden captures against renders: (0.5, 0.5) px on every view")]
    public const float PixelCenterOffset = 0.5f;

    /// <summary>Near clip of the view, in uu.</summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 NEAR_CLIPPING_PLANE is 1 uu; a little more keeps depth precision")]
    public const float NearClipUnits = 2f;

    /// <summary>Far clip of the view, in uu (UE2 draws to the end of the world box).</summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 WORLD_MAX 524288; 65536 covers every converted map")]
    public const float FarClipUnits = 65536f;
}
