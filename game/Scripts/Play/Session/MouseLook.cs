using System;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScctGame.Play.Session;

/// <summary>
/// Mouse look as Reloaded scales it, step by step (spec/native/reloaded-features-from-code.md rule 23):
/// <list type="number">
/// <item>Reloaded reads the mouse once a frame (DirectInput GetDeviceState, 0x100b6920), so the axis event carries the
/// frame's counts.</item>
/// <item>The axis binding: <c>aMouseX = counts x Speed x 0.1</c> (UInput axis exec, game 0x109fc165), with every axis
/// binding's Speed forced to 1.0 by Reloaded (parser hook at game 0x109fda59; the stock INI's 6.00 is ignored).</item>
/// <item>Reloaded's look multiplier (hook at game 0x109fc177, 0x100b6d00): <c>aMouseX x multiplier / 10</c>.</item>
/// <item>UInput::ReadInput scales every input float by 2 / DeltaTime (game 0x109fea16), which cancels the DeltaTime
/// of the rotation code, so the turn per count does not depend on the frame time.</item>
/// <item>The native mapping to 640 x 480: aTurn = SizeX aMouseX / 640, aLookUp = SizeY aMouseY / 480; fTurnValue is
/// 0.75 aTurn (<see cref="OpenScct.Game.Movement.ControlInput"/>).</item>
/// <item>Reloaded sets fSensibiliteSouris every frame (0x10019b20): 4.0, or 0.4 in a camera view where it also scales
/// aTurn and aLookUp by the camera setting x 10. The profile's MouseSensitivity has no effect in Reloaded.</item>
/// </list>
/// So a count turns <c>3 x multiplier x SizeX / 640</c> rotation units (spy free look 1.4 at 1920 wide: 12.6), at
/// any tick rate. <see cref="MouseSensitivity"/> is ours on top: 40 (the default) is Reloaded's speed, other values
/// scale it in proportion.
/// </summary>
public sealed record MouseLook
{
    /// <summary>The axis binding speed Reloaded forces on every axis binding (the stock INI says 6.00).</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100b6d30 at game 0x109fda59: the axis binding's SPEED= value is replaced by 1.0 (0x102bfce8)")]
    public float AxisSpeed { get; init; } = 1f;

    /// <summary>The axis exec's scale of a mouse event (aMouseX = Delta x Invert x Speed x 0.1).</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x109fc165..0x109fc171: fld Delta, fimul Invert, fmul Speed, fmul 0.1 (0x10c109e8), IST_Axis branch")]
    public const float AxisEventScale = 0.1f;

    /// <summary>Reloaded divides its look multiplier by this before scaling the axis.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100b6d15: fmul multiplier, fdiv 10.0 (0x102bfcf4)")]
    public const float MultiplierDivisor = 10f;

    /// <summary>UInput::ReadInput multiplies every input float by this over the frame's DeltaTime.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x109fea16: Scale = 2.0 (0x10c10acc) / DeltaSeconds, applied to each CPF_Input FloatProperty")]
    public const float ReadInputScale = 2f;

    /// <summary>The fSensibiliteSouris Reloaded writes every frame outside camera views.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x10019b20: PlayerController+0x5cc (fSensibiliteSouris) = 4.0 (0x40800000)")]
    public const float ReloadedSensibilite = 4f;

    /// <summary>The fSensibiliteSouris Reloaded writes in a camera view (sticky camera, camera network).</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x10019b20: fSensibiliteSouris = 0.4 (0x3ecccccd) and aTurn, aLookUp x camera setting x 10 when the camera-view flag 0x1036abbc is set")]
    public const float CameraSensibilite = 0.4f;

    /// <summary>The camera view's axis scale over the camera setting (with <see cref="CameraSensibilite"/>).</summary>
    public const float CameraAxisScale = 10f;

    /// <summary>The response curves' divisors of the spy's mouse free look (Linear, CurveSoft, CurveMedium, CurveAggressive).</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100b6c39: free look / table[curve] (0x103411b4, stride 8: 1.0, 0.5, 0.4, 0.3333)")]
    private static readonly float[] CurveDivisors = [1f, 0.5f, 0.4f, 1f / 3f];

    /// <summary>The curves in <see cref="CurveDivisors"/> order (the settings' Responses).</summary>
    private static readonly string[] CurveNames = ["Linear", "CurveSoft", "CurveMedium", "CurveAggressive"];

    /// <summary>The mouse sensitivity that gives Reloaded's speed (Reloaded's own profile default).</summary>
    public const float ReloadedMouseSensitivity = 40f;

    /// <summary>
    /// Rotation units a count turns at a 640-pixel-wide viewport with a multiplier of 1 and the default sensitivity:
    /// 0.75 x 0.1 x Speed / 10 x 2 x 4 x 50 = 3.
    /// </summary>
    public static float UnitsPerCount => OpenScct.Game.Movement.MovementK.LookScale * AxisEventScale / MultiplierDivisor * ReadInputScale
        * ReloadedSensibilite * OpenScct.Game.Movement.MovementK.RotationRateScale;

    /// <summary>The look sensitivity on top of Reloaded's (40 = Reloaded; other values scale the turn in proportion).</summary>
    [Provenance(ProvenanceKind.Estimated, "ours: Reloaded overrides the profile's MouseSensitivity every frame, so the slider is kept as a scale where its default 40 is Reloaded's speed")]
    public float MouseSensitivity { get; init; } = ReloadedMouseSensitivity;

    /// <summary>The look mode of the controller groups (GamepadInput's counts): the vertical scales apply, as Reloaded's pad path does.</summary>
    public bool Pad { get; init; }

    /// <summary>The spy mouse free look's response curve (Linear, CurveSoft, CurveMedium or CurveAggressive).</summary>
    public string SpyFreeLookResponse { get; init; } = "Linear";

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

    /// <summary>The fSensibiliteSouris Reloaded gives the controller in its current view.</summary>
    public static float SensibiliteFor(SPlayerController pc) => InCameraView(pc) ? CameraSensibilite : ReloadedSensibilite;

    /// <summary>
    /// Writes the look axes of one tick: aMouseX and aMouseY from the counts, then aTurn and aLookUp by the input
    /// mapping for a viewport of <paramref name="sizeX"/> by <paramref name="sizeY"/> pixels; and Reloaded's
    /// sensitivity and the profile's spy X inversion.
    /// </summary>
    public void Apply(SPlayerController pc, float countsX, float countsY, int sizeX, int sizeY)
    {
        ArgumentNullException.ThrowIfNull(pc);
        (pc.aMouseX, pc.aMouseY, pc.aTurn, pc.aLookUp) = Axes(pc, countsX, countsY, sizeX, sizeY);
        pc.fSensibiliteSouris = SensibiliteFor(pc);
        pc.bInvertHorizontalAxisCamSpy = InvertXSpy;
    }

    /// <summary>
    /// The aMouseX, aMouseY, aTurn and aLookUp <see cref="Apply"/> would write for these counts (one tick's worth),
    /// without writing them. aTurn and aLookUp include Reloaded's camera-view scale (camera setting x 10).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "aMouseX = counts x Speed x 0.1 x multiplier / 10 x 2 / DeltaTime (game 0x109fc165, Reloaded 0x100b6d00, ReadInput 0x109fea16); aTurn = SizeX aMouseX / 640 (game 0x10aaa919)")]
    [Provenance(ProvenanceKind.Estimated, "ReadInput's DeltaTime is our fixed tick (the counts are summed per tick, as Reloaded sums them per frame)")]
    public (float MouseX, float MouseY, float Turn, float LookUp) Axes(SPlayerController pc, float countsX, float countsY, int sizeX, int sizeY)
    {
        ArgumentNullException.ThrowIfNull(pc);
        (float h, float v) = Multipliers(pc);
        float user = Math.Max(0f, MouseSensitivity) / ReloadedMouseSensitivity;
        float k = AxisSpeed * AxisEventScale / MultiplierDivisor * (ReadInputScale / OpenScct.Core.Sim.DeltaTime) * user * Scale * Clutch;
        float mouseX = countsX * k * h;
        float mouseY = (InvertY ? -countsY : countsY) * k * v;
        float view = InCameraView(pc) ? CameraAxisScale : 1f;
        return (mouseX, mouseY, Math.Max(1, sizeX) * mouseX / 640f * view, Math.Max(1, sizeY) * mouseY / 480f * view);
    }

    /// <summary>
    /// Reloaded's sideways and vertical look multipliers for the mouse in the view the controller is in
    /// (Reloaded.Core 0x100b6b70, mouse path):
    /// <list type="bullet">
    /// <item>a camera view (sticky camera, camera network): the side's <c>cam</c> (with the camera axis scale and
    /// sensitivity, <see cref="Axes"/>);</item>
    /// <item>the merc: <c>look</c>, times <c>scope</c> in the sniper scope, the vertical also times
    /// <c>scope_vertical</c>;</item>
    /// <item>the spy: <c>gun_look</c> with the gun drawn (WeaponLoaded 2), else <c>free_look</c> over its response
    /// curve's divisor; times <c>binoculars</c> under the binoculars.</item>
    /// </list>
    /// The mouse ignores the groups' <c>vertical</c>; the controller groups (<see cref="Pad"/>) keep it.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100b6b70 (mouse: cl = 0): spy group 0x103ff9b0 (free 9b0, gun 9b4, sticky cam 9bc, binoculars 9c0, free curve 9cc), merc group 0x103ffa04 (look a04, cam net a0c, scope a10, scope vertical a14); bBinocularVision (0xc38 & 0x2000000) multiplies; vertical only on the pad path")]
    [Provenance(ProvenanceKind.Estimated, "controller groups: the vertical scale relative to the side's default (3.5 spy, 1.0 merc), as before")]
    public (float Horizontal, float Vertical) Multipliers(SPlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        bool merc = pc.SBPawn is SPAWNDEFENSE;
        if (InCameraView(pc))
        {
            float cam = merc ? MercCam : SpyCam;
            return (cam, cam);
        }

        float vertical = !Pad ? 1f : merc ? MercVertical / MercVerticalDefault : SpyVertical / SpyVerticalDefault;
        if (merc)
        {
            float look = MercLook;
            return pc.bBinocularVision ? (look * MercScope, look * MercScope * MercScopeVertical) : (look, look * vertical);
        }

        float spy = (pc.SBPawn?.WeaponLoaded ?? 0) == 2 ? SpyGunLook : SpyFreeLook / CurveDivisor(SpyFreeLookResponse);
        return pc.bBinocularVision ? (spy * SpyScope, spy * SpyScope) : (spy, spy * vertical);
    }

    private static bool InCameraView(SPlayerController pc) =>
        pc.SBPawn?.UsedObject is SOrientableObject && GadgetStates.InStickyCamViewState(pc);

    private float CurveDivisor(string response)
    {
        if (Pad)
        {
            // The controller's curves are GamepadInput's own response shaping.
            return 1f;
        }

        int i = Array.IndexOf(CurveNames, response);
        return i < 0 ? 1f : CurveDivisors[i];
    }
}
