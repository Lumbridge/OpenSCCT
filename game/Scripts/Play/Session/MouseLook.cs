using System;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScctGame.Play.Session;

/// <summary>
/// Mouse look as the original's input path scales it: the binding's axis speed (<c>IK_MouseX=Axis aMouseX
/// Speed=6.00</c>), Reloaded's per-side look multipliers, the native input mapping to a 640 x 480 reference
/// (aTurn = SizeX aMouseX / 640, aLookUp = SizeY aMouseY / 480, spec/native/input.md rule 1.1) and the profile's
/// mouse sensitivity (fSensibiliteSouris = MouseSensitivity / 10, input-and-rotation.md).
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

    /// <summary>A user multiplier over everything (1 keeps the original's scaling).</summary>
    public float Scale { get; init; } = 1f;

    public bool InvertY { get; init; }

    /// <summary>The profile value the controller reads (MouseSensitivity / 10, clamped to the profile's 4..40).</summary>
    public float SensibiliteSouris => Math.Clamp(MouseSensitivity, 4f, 40f) / 10f;

    /// <summary>
    /// Writes the look axes of one tick: aMouseX and aMouseY from the counts, then aTurn and aLookUp by the input
    /// mapping for a viewport of <paramref name="sizeX"/> by <paramref name="sizeY"/> pixels.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Where Reloaded applies its look multipliers inside NativePlayerInput is not decoded; here they scale aMouseX and aMouseY")]
    public void Apply(SPlayerController pc, float countsX, float countsY, int sizeX, int sizeY)
    {
        ArgumentNullException.ThrowIfNull(pc);
        (pc.aMouseX, pc.aMouseY, pc.aTurn, pc.aLookUp) = Axes(pc, countsX, countsY, sizeX, sizeY);
        pc.fSensibiliteSouris = SensibiliteSouris;
    }

    /// <summary>The aMouseX, aMouseY, aTurn and aLookUp <see cref="Apply"/> would write for these counts, without writing them.</summary>
    public (float MouseX, float MouseY, float Turn, float LookUp) Axes(SPlayerController pc, float countsX, float countsY, int sizeX, int sizeY)
    {
        ArgumentNullException.ThrowIfNull(pc);
        float group = pc.SBPawn is SPAWNDEFENSE ? MercLook : (pc.SBPawn?.WeaponLoaded ?? 0) > 0 ? SpyGunLook : SpyFreeLook;
        float k = AxisSpeed * group * Scale;
        float mouseX = countsX * k;
        float mouseY = (InvertY ? -countsY : countsY) * k;
        return (mouseX, mouseY, Math.Max(1, sizeX) * mouseX / 640f, Math.Max(1, sizeY) * mouseY / 480f);
    }
}
