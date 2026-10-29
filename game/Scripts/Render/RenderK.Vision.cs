using Godot;
using OpenScct.Provenance;

namespace OpenScctGame.Render;

/// <summary>
/// Vision mode, torch and effect numbers of the Faithful renderer (docs/RENDERING.md#vision-modes,
/// #spot-shadow-lights, #emitters). The game addresses are of the game DLL (System/SCCT Versus, base 0x10900000).
/// </summary>
public static partial class RenderK
{
    // ---- RendMap codes ---------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Measured, "live RendMap reads")]
    public const int RendMapThermal = 36;

    [Provenance(ProvenanceKind.Measured, "live RendMap reads")]
    public const int RendMapMotion = 37;

    [Provenance(ProvenanceKind.Measured, "live RendMap reads")]
    public const int RendMapEmf = 38;

    /// <summary>SGoggle mode 50: the binocular overlay flag; the renderer keeps drawing the mode under it.</summary>
    [Provenance(ProvenanceKind.Decoded, "SGoggle consts, GoggleDown.SwitchGoggle (spec/equipment/vision-goggles.md)")]
    public const int RendMapBinoculars = 50;

    // ---- night -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Night vision draws its scene with a light boost: the pre-render step sets the render interface's byte at +0xC to
    /// 30 for RendMap 34 (int(SquintingFactor x 28) otherwise, 0 in play). For a lightmapped surface EnableLighting puts
    /// the byte halved (15) into the texture factor and ADDs it to the lightmap stage, which the diffuse stage modulates
    /// 2x, so the light term grows by 2 x 15 / 255. Pawns and vertex-lit meshes take the same: with this value on every
    /// lit surface and no glow gate (the night pass reads the scene before the glow composite) the twelve SteeE night
    /// captures match at median SSIM 0.948 (4 x 4 blocks) and luma ratio 1.01, and the spy at the spawn reads 4 / 7 / 31
    /// (10th, 50th, 90th percentile of green) as in the game; the actor path's own use of the byte is not decoded.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "pre-render 0x10a0af70: RI+0xC = 0x1E for RendMap 34")]
    [Provenance(ProvenanceKind.Decoded, "editor RenderingModeNormal::EnableLighting 0x10f2a010, FD3DRenderInterface::SetSimpleMaterial 0x10f38d30: TFACTOR = byte >> 1, stage 0 ADD(lightmap, TFACTOR)")]
    [Provenance(ProvenanceKind.Measured, "SteeE night captures (tools/golden capture_game.py --rendmap 34): the same term on actors and vertex-lit meshes")]
    public const float NightSceneBoost = 2f * (30 >> 1) / 255f;

    // ---- thermal ---------------------------------------------------------------------------------------------

    /// <summary>The Z pass writes B = 1 - viewZ / 5000 uu (the table's v), clamped.</summary>
    [Provenance(ProvenanceKind.Decoded, "thermal Z pass ps_1_1 0x34e3d8 (B = 1 - t2), texture coordinate scale 1/5000 (bridge docs/VISION_MODES.md)")]
    public const float ThermalFadeUnits = 5000f;

    /// <summary>HEATTEX_Cool heat = 4 x lum(heat texture) x HeatIntensity.</summary>
    [Provenance(ProvenanceKind.Decoded, "thermal Z pass ps_1_1 0x34e508: dp3 r0 x4, t0, (0.3, 0.59, 0.11); mul by v0")]
    public const float ThermalCoolGain = 4f;

    /// <summary>The heat blur: two runs of a vertical then horizontal 4-tap filter, taps at +-1 and +-3 texels.</summary>
    [Provenance(ProvenanceKind.Decoded, "blur run 0x10967d70 offsets (0, +-1), (0, +-3) then (+-1, 0), (+-3, 0); ps_1_1 0x34e9b8 weights")]
    public const float BlurNearWeight = 0.332031f;

    [Provenance(ProvenanceKind.Decoded, "ps_1_1 0x34e9b8 c1")]
    public const float BlurFarWeight = 0.167969f;

    /// <summary>
    /// Sizes of the blur targets at the default detail: run 1 draws the frame into 512 x 256 (vertical taps) then
    /// 256 x 256 (horizontal), run 2 draws that into 256 x 128 then 128 x 128. A high-detail switch doubles them for
    /// frames wider than 800 pixels.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "render target sizes 0x1090ab98 (defaults) and 0x1095c330 (detail switch); targets 0x30e6c..0x30e78")]
    public static readonly Vector2I[] BlurTargets = [new(512, 256), new(256, 256), new(256, 128), new(128, 128)];

    /// <summary>
    /// The detail switch of the post targets (blur pyramid and glow bright target) for a frame <paramref name="width"/>
    /// pixels wide: with the switch on, the widths double above 640 and the heights above 800 (the x sizes come from
    /// 0x10c78b5c, the y sizes from 0x10c78ba0). At the default sizes a 2000-pixel frame blurred the heat four times wider
    /// than the original's captures: the pawn's hot spots stopped at yellow, the head read cold and a wide blue halo
    /// ringed the body.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "0x1095c330: 0x10c78b98 == 1 && width > 0x280 -> 0x10c78b5c = 0x10c78b80 = 0x200, and width > 800 -> 0x10c78ba0 = 0x200 (else 0x100); targets from them x 2, / 2, >> 2, >> 3")]
    [Provenance(ProvenanceKind.Estimated, "the switch 0x10c78b98 (set by the render device's vtable slot at 0x10c0c254) taken as on: Default.ini HighDetailActors=True, and the original's thermal halo in 2000-pixel captures is the doubled targets' width")]
    public static Vector2I PostDetailScale(int width) =>
        new(PostHighDetail && width > 640 ? 2 : 1, PostHighDetail && width > 800 ? 2 : 1);

    /// <summary>The render device's high-detail switch for the post targets (see <see cref="PostDetailScale"/>).</summary>
    public const bool PostHighDetail = true;

    /// <summary>
    /// The composite of the two blurred levels back over the sharp heat: frame = lerp(sharp, lerp(blur1, blur2, 128/255),
    /// 200/255) (fixed-function BLENDFACTORALPHA with factor 128, frame blend SRCALPHA with vertex alpha 200).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "composite 0x10967e30 called with 0xC8 (vertex alpha) and 0x80 (texture factor) from 0x10964f2e")]
    public const float ThermalBlurAlpha = 200f / 255f;

    [Provenance(ProvenanceKind.Decoded, "composite 0x10967e30 texture factor 0x80")]
    public const float ThermalLevelMix = 128f / 255f;

    /// <summary>The thermal post: out = 2 x table(G, B) x noise.</summary>
    [Provenance(ProvenanceKind.Decoded, "ps_1_1 0x34e11c: texreg2gb t2, t0; mul_x2 r0, t2, t1")]
    public const float ThermalLutGain = 2f;

    /// <summary>Default per-actor heat values (Actor class defaults): HeatIntensity 0, HeatOpacity 1.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.Actor defaults (shared/classes.json)")]
    public const float DefaultHeatOpacity = 1f;

    /// <summary>Pawn HeatIntensity (SPawnAttaque and SPAWNDEFENSE defaults).</summary>
    [Provenance(ProvenanceKind.Decoded, "SBase.SPawnAttaque / SPAWNDEFENSE defaults HeatIntensity 1.0")]
    public const float PawnHeatIntensity = 1f;

    // ---- EMF -------------------------------------------------------------------------------------------------

    /// <summary>EMF post: out = 2 x (1 - scene) x noise x sonar.</summary>
    [Provenance(ProvenanceKind.Decoded, "ps_1_1 0x34efe0 (slot 0x30fb4): mul_x2 r0, 1-t0, t1; mul r0, r0, t2")]
    public const float EmfGain = 2f;

    /// <summary>The EMF frame's clear colour (D3D 0xffffda85).</summary>
    [Provenance(ProvenanceKind.Decoded, "UGameEngine::Draw clear colour by RendMap, 0x109ec4c2..0x109ec530")]
    public static readonly Color EmfClear = new(1f, 0xDA / 255f, 0x85 / 255f);

    /// <summary>The environment map lookup by camera-space reflection: uv = 0.25 R.xy + 0.5.</summary>
    [Provenance(ProvenanceKind.Estimated, "bridge M_PP_EMF (camera-space reflection texgen; the texture matrix is not decoded)")]
    public const float EmfReflectionScale = 0.25f;

    /// <summary>EMFOpacity below this draws translucent; 0 hides the actor.</summary>
    [Provenance(ProvenanceKind.Decoded, "bridge docs/VISION_MODES.md (EMFOpacity rules)")]
    public const float EmfOpaqueFrom = 0.99f;

    // ---- motion ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Motion post (shader 0x34e278 with constants from LevelInfo.MTVSlope and MTVOffset): lum = 2 s L(blurred scene x
    /// noise) + MTVOffset / 255 with s = 1 + 3 MTVSlope / 255; below 0.5765 a ramp from navy to red, above it the fold
    /// 0.9 (1 - lum) (red) and 0.05 (1 - lum) (green, blue); times the sonar backdrop.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "motion pass constants 0x10963f9f..0x10964127, ps_1_1 0x34e278")]
    public static readonly Color MotionLow = new(0f, 20f / 255f, 40f / 255f);

    [Provenance(ProvenanceKind.Decoded, "motion pass c3 0x10964076..0x109640a2")]
    public static readonly Color MotionHigh = new(180f / 255f, 10f / 255f, 10f / 255f);

    [Provenance(ProvenanceKind.Decoded, "motion pass c2.a 4 x 0.43367 = 1.7347")]
    public const float MotionRampScale = 1.7347f;

    [Provenance(ProvenanceKind.Decoded, "motion pass c4.a 0.0765 + cnd 0.5")]
    public const float MotionFoldAt = 0.5765f;

    [Provenance(ProvenanceKind.Decoded, "motion pass c4.rgb (0.45, 0.025, 0.025) x2")]
    public static readonly Vector3 MotionFold = new(0.9f, 0.05f, 0.05f);

    /// <summary>
    /// The clean view rectangles of motion vision (ps_1_1 at 0x10c4e328, drawn per rectangle after the motion pass,
    /// 0x10964996..0x10964c7b): out = 2 x (2 x (L(scene) tint + bias)) x grain x sonar, with L = dot(scene, (0.3, 0.59,
    /// 0.11)) of the sharp scene copy (RenderDevice +0x30e24, not the blurred level), the grain and sonar of the motion
    /// pass at the same screen place (the sonar tiled 80 x 60 over the frame).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "ps 0x10c4e328: def c5 0.675, 0.75, 0.675; mad_x2 then mul_x2")]
    public static readonly Vector3 MotionCleanTint = new(0.675f, 0.75f, 0.675f);

    [Provenance(ProvenanceKind.Decoded, "ps 0x10c4e328: def c6 0.09, 0.1, 0.09")]
    public static readonly Vector3 MotionCleanBias = new(0.09f, 0.1f, 0.09f);

    /// <summary>The x2 of the mad and the x2 of the grain multiply.</summary>
    [Provenance(ProvenanceKind.Decoded, "ps 0x10c4e328: mad_x2 r0, r0, c5, c6; mul_x2 r0, r0, t1")]
    public const float MotionCleanGain = 2f;

    /// <summary>LevelInfo.MTVSlope and MTVOffset class defaults (85, 52); maps override them (SteeE 8, 8).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.LevelInfo defaults")]
    public const int DefaultMtvSlope = 85;

    [Provenance(ProvenanceKind.Decoded, "Engine.LevelInfo defaults")]
    public const int DefaultMtvOffset = 52;

    // ---- grain and sonar -------------------------------------------------------------------------------------

    /// <summary>
    /// Grain: Engine.Noise0_gauss tiled one texel a pixel, turned by a random angle of [0, 1) radian and moved by a
    /// random offset of [0, 1) each frame.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "thermal post 0x10965191..0x109652a8: rand() / 32768 for the angle and both offsets")]
    public const float NoiseAngleRange = 1f;

    /// <summary>The sonar backdrop is tiled 80 x 60 times over the frame.</summary>
    [Provenance(ProvenanceKind.Decoded, "motion pass stage 2 texture coordinates 80.0 and 60.0, 0x1096473c..0x10964770")]
    public static readonly Vector2 SonarTiles = new(80f, 60f);

    // ---- merc torch (SGadTorche) -----------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SBase.SGadTorche defaults EchelonRange")]
    public const float TorchRange = 1500f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGadTorche defaults SpotWidth / SpotHeight")]
    public const float TorchSpotSize = 400f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGadTorche defaults NearPlaneRatio")]
    public const float TorchNearPlaneRatio = 0.0025f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGadTorche defaults LightBrightness, LightHue, LightSaturation")]
    public const float TorchBrightness = 304f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGadTorche defaults")]
    public const byte TorchHue = 21;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGadTorche defaults")]
    public const byte TorchSaturation = 255;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGadTorche defaults EchelonBias")]
    public const float TorchShadowBias = 0.00018f;

    /// <summary>EchelonLightingMaterial AttenuationType of Echelon.Torche: EchAtten_Linear.</summary>
    [Provenance(ProvenanceKind.Decoded, "Echelon.utx Torche: AttenuationType 1, ProjectedLightingTexture flashlight")]
    public const int TorchAttenuation = 1;

    /// <summary>
    /// Gain of a spot shadow light's projected term on the 2x light stage. The Echelon light pass is not decoded; 2 is
    /// the gain of every other light the original adds to a surface.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "same 2x stage as the point lights (RenderingModeNormal::SetLight); not measured")]
    public const float SpotShadowGain = 2f;

    /// <summary>
    /// Cap of a spot shadow light's brightest colour channel (the colour is scaled down, keeping its hue; FactD's
    /// orange window patches, docs/RENDERING.md#spot-shadow-lights), before <see cref="SpotShadowGain"/>: a colour handed to the
    /// fixed-function or ps_1_x stages is clamped to [0, 1], so a light far above brightness 310 (0.8213 x Br / 255 = 1)
    /// lights no more than one at 310. The bridge measured the game against this: RiveD's Light3248 at brightness 3000
    /// lifts the spawn's luma only from 20 to 35, and its tuned projector cap (brightness 620 at gain 1) is the same
    /// light term (2) as this cap at gain 2. The merc torch (304) is below it.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "bridge docs/LIGHTING_BALANCE.md 'Projector brightness cap' (RiveD, MissD survey against the game); the Echelon light pass is not decoded")]
    public const float SpotShadowColourCap = 1f;

    /// <summary>
    /// Shadow map height of a spot shadow light; its width follows SpotWidth / SpotHeight, up to twice this (a wider
    /// light gets a lower height instead).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "the original's ShadowZBuffer sizes are not decoded")]
    public const int SpotShadowMapSize = 512;

    /// <summary>
    /// Spot shadow lights drawn at once: the template's slots (each with a tile of the shared shadow atlas). The
    /// original's limit is not decoded.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "a frame budget: one shadow pass per slot per frame")]
    public const int SpotShadowSlots = 8;

    /// <summary>Slots map LE_ESpotShadow lights may take (the nearest to the camera); the rest stay for torches and searchlights.</summary>
    [Provenance(ProvenanceKind.Estimated, "a frame budget")]
    public const int SpotShadowMapLights = 6;

    /// <summary>
    /// Radius in shadow map texels of the eight-lookup disc that softens spot shadow edges, as the sandbox's Reloaded
    /// config does (shadow_edge_smoothing: "a disc of eight blended lookups"); 0 keeps the stock single filtered lookup.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Reloaded shadow_edge_smoothing (spec/reloaded/FEATURES.md rl:gfx.shadows); the disc radius is not decoded")]
    public const float SpotShadowSoftRadius = 1.5f;

    /// <summary>Side of a spot light's projected texture in the template's texture array.</summary>
    [Provenance(ProvenanceKind.Estimated, "the cookies are resampled to one size to share an array")]
    public const int SpotCookieSize = 256;

    /// <summary>
    /// NearPlaneRatio from which the frustum's apex (p x EchelonRange / (1 - p) behind the light) is treated as at
    /// infinity: parallel rays, an orthographic box SpotWidth x SpotHeight (FactD's Light17985 has 1.0).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "the limit of the decoded frustum (editor 0x111a5480) as p tends to 1")]
    public const float SpotParallelRatio = 0.999f;

    // ---- coronas --------------------------------------------------------------------------------------------

    /// <summary>
    /// The corona's colour (corona colour x 0.5 x Fade, decoded) is doubled when drawn: the halo of SteeE's crane lamp
    /// reads 1.9 to 2.1 times our undoubled halo at every radius, as from a Modulate2X stage on the halved colour.
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "SteeE crane golden: radial profile of Light7388's corona against the capture")]
    public const float CoronaStageGain = 2f;

    // ---- emitters --------------------------------------------------------------------------------------------

    /// <summary>Fixed step of the emitter simulation, so a scene time always gives the same particles.</summary>
    [Provenance(ProvenanceKind.Estimated, "60 Hz, the simulation tick")]
    public const float EmitterStep = 1f / 60f;
}
