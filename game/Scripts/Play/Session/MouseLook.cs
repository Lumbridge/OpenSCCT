using System;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScctGame.Play.Session;

/// <summary>
/// Mouse look as the original's input path scales it: the binding's axis speed (<c>IK_MouseX=Axis aMouseX
/// Speed=6.00</c>), Reloaded's per-side and per-view look multipliers (rl:input.sens: free look, gun look, scope or
/// binoculars, sticky camera and camera network, the vertical scales, and a held clutch), the native input mapping to a
/// 640 x 480 reference (aTurn = SizeX aMouseX / 640, aLookUp = SizeY aMouseY / 480, spec/native/input.md rule 1.1) and
/// the profile's mouse sensitivity (fSensibiliteSouris = MouseSensitivity / 10, input-and-rotation.md).
/// </summary>
public sealed record MouseLook
{
    /// <summary>The axis binding speed of Default_kbd_0.ini and Default_kbd_1.ini.</summary>
    [Provenance(ProvenanceKind.Decoded, "Default_kbd_0.ini / Default_kbd_1.ini: IK_MouseX=Axis aMouseX Speed=6.00")]
    public float AxisSpeed { get; init; } = 6f;

    /// <summary>The profile's MouseSensitivity (4 to 40); the Reloaded profile default is 40.</summary>
    [Provenance(ProvenanceKind.Decoded, "DefaultProfile.ini MouseSensitivity=40.0")]
    public float MouseSensitivity { get; init; } = 40f;

    /// <summary>Reloaded spy free look (gun holstered), controls_spy_config_kbm.free_look.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:input.sens: spy kbm free_look 1.4")]
    public float SpyFreeLook { get; init; } = 1.4f;

    /// <summary>Reloaded spy gun look (gun drawn), controls_spy_config_kbm.gun_look.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:input.sens: spy kbm gun_look 1.0")]
    public float SpyGunLook { get; init; } = 1f;

    /// <summary>Reloaded merc look, controls_merc_config_kbm.look.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:input.sens: merc kbm look 1.0")]
    public float MercLook { get; init; } = 1f;

    /// <summary>The spy's binoculars (controls_spy_config_kbm.scope), in place of the look multiplier.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:input.sens: spy kbm scope 0.75")]
    public float SpyScope { get; init; } = 0.75f;

    /// <summary>The merc's sniper scope (controls_merc_config_kbm.scope), in place of the look multiplier.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:input.sens: merc kbm scope 0.75")]
    public float MercScope { get; init; } = 0.75f;

    /// <summary>
    /// The merc scope's vertical scale (scope_vertical): the stock scope turns up and down at about 40.8 % of its
    /// sideways speed, and 2.45 brings that back to 1:1; 1 is Reloaded's legacy sniper mode (the stock speed).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:input.sens: merc kbm scope_vertical 2.45; 2.45 x 0.408 = 1")]
    public float MercScopeVertical { get; init; } = 2.45f;

    /// <summary>The spy's vertical look scale (controls_spy_config_kbm.vertical).</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:input.sens: spy kbm vertical 3.5")]
    public float SpyVertical { get; init; } = SpyVerticalDefault;

    /// <summary>The merc's vertical look scale (controls_merc_config_kbm.vertical).</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:input.sens: merc kbm vertical 1.0")]
    public float MercVertical { get; init; } = MercVerticalDefault;

    public const float SpyVerticalDefault = 3.5f;

    public const float MercVerticalDefault = 1f;

    /// <summary>The spy's sticky camera and camera network (controls_spy_config_kbm.cam).</summary>
    public float SpyCam { get; init; } = 1f;

    /// <summary>The merc's camera views (controls_merc_config_kbm.cam).</summary>
    public float MercCam { get; init; } = 1f;

    /// <summary>The held sensitivity clutches' multiplier (rl:input.clutch), 1 when none is held.</summary>
    public float Clutch { get; init; } = 1f;

    /// <summary>A user multiplier over everything (1 keeps the original's scaling).</summary>
    public float Scale { get; init; } = 1f;

    public bool InvertY { get; init; }

    /// <summary>Profile InvertX: the spy's free look turns the other way (bInvertHorizontalAxisCamSpy).</summary>
    public bool InvertXSpy { get; init; }

    /// <summary>The profile value the controller reads (MouseSensitivity / 10, clamped to the profile's 4..40).</summary>
    public float SensibiliteSouris => Math.Clamp(MouseSensitivity, 4f, 40f) / 10f;

    /// <summary>
    /// Writes the look axes of one tick: aMouseX and aMouseY from the counts, then aTurn and aLookUp by the input
    /// mapping for a viewport of <paramref name="sizeX"/> by <paramref name="sizeY"/> pixels; and the profile's
    /// sensitivity and spy X inversion, which SPlayerProfile puts on the controller.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Where Reloaded applies its look multipliers inside NativePlayerInput is not decoded; here they scale aMouseX and aMouseY")]
    public void Apply(SPlayerController pc, float countsX, float countsY, int sizeX, int sizeY)
    {
        ArgumentNullException.ThrowIfNull(pc);
        (pc.aMouseX, pc.aMouseY, pc.aTurn, pc.aLookUp) = Axes(pc, countsX, countsY, sizeX, sizeY);
        pc.fSensibiliteSouris = SensibiliteSouris;
        pc.bInvertHorizontalAxisCamSpy = InvertXSpy;
    }

    /// <summary>The aMouseX, aMouseY, aTurn and aLookUp <see cref="Apply"/> would write for these counts, without writing them.</summary>
    public (float MouseX, float MouseY, float Turn, float LookUp) Axes(SPlayerController pc, float countsX, float countsY, int sizeX, int sizeY)
    {
        ArgumentNullException.ThrowIfNull(pc);
        (float h, float v) = Multipliers(pc);
        float k = AxisSpeed * Scale * Clutch;
        float mouseX = countsX * k * h;
        float mouseY = (InvertY ? -countsY : countsY) * k * v;
        return (mouseX, mouseY, Math.Max(1, sizeX) * mouseX / 640f, Math.Max(1, sizeY) * mouseY / 480f);
    }

    /// <summary>
    /// The sideways and vertical look multipliers of the view the controller is in: a camera view (sticky camera,
    /// camera network) takes <c>cam</c>; the binoculars or the sniper scope take <c>scope</c> (the merc's vertical
    /// also <c>scope_vertical</c>); otherwise the look of the side (the spy's free or gun look) with its
    /// <c>vertical</c> scale.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "rl:input.sens open question: the meaning of vertical is not decoded; it scales the vertical look relative to the side's default (3.5 spy, 1.0 merc), so the defaults keep the stock ratio")]
    public (float Horizontal, float Vertical) Multipliers(SPlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        bool merc = pc.SBPawn is SPAWNDEFENSE;
        if (pc.SBPawn?.UsedObject is SOrientableObject && GadgetStates.InStickyCamViewState(pc))
        {
            float cam = merc ? MercCam : SpyCam;
            return (cam, cam);
        }

        if (pc.bBinocularVision)
        {
            float scope = merc ? MercScope : SpyScope;
            return (scope, merc ? scope * MercScopeVertical : scope);
        }

        float look = merc ? MercLook : (pc.SBPawn?.WeaponLoaded ?? 0) > 0 ? SpyGunLook : SpyFreeLook;
        float vertical = merc ? MercVertical / MercVerticalDefault : SpyVertical / SpyVerticalDefault;
        return (look, look * vertical);
    }
}
