using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The merc's aim point on the screen: the part of ASHud::execHudDef (game 0x10af8060) that runs before the Enhanced
// Reality markers and the motion tracking, the unnamed native at game 0x10af7cf0 (spec/hud/merc-reticle.md rules
// 3a to 3d). The merc page centres its reticle and its reticle message on SpotReticule.
public partial class SHud
{
    /// <summary>The aim point is this far along MyDesiredRotation from the pawn's eye.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10af7f03: the vector of MyDesiredRotation (SPlayerController +0x8bc) times 15000.0 (0x10c10ee0)")]
    public const float SpotReticuleDistance = 15000f;

    /// <summary>The canvas the aim point is written in: the HUD's 640 x 480 page, stretched over the screen.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10af802c: the projected point through FCanvasUtil's screen-to-canvas matrix (Canvas.pCanvasUtil +0x14); the merc page places A_Reticule in 640 x 480 units with it")]
    public const float SpotReticuleCanvasWidth = 640f;

    [Provenance(ProvenanceKind.Decoded, "as SpotReticuleCanvasWidth")]
    public const float SpotReticuleCanvasHeight = 480f;

    /// <summary>
    /// The aim point of HudDef (rule 3b): the pawn's eye, as the native reads it (Location + WalkBob + EyeHeight on Z,
    /// not the script's EyePosition), plus 15000 uu along the controller's MyDesiredRotation: the line the rifle's
    /// trace and the frag's throw follow.
    /// </summary>
    [PortOf("nat:SBase.SHud.HudDef", Spec = "spec/hud/merc-reticle.md#rules")]
    public static FVector SpotReticuleAimPoint(Pawn P, SPlayerController PC)
    {
        ArgumentNullException.ThrowIfNull(P);
        ArgumentNullException.ThrowIfNull(PC);
        PC.MyDesiredRotation.GetAxes(out FVector x, out _, out _);
        return P.Location + P.WalkBob + new FVector(0f, 0f, P.EyeHeight) + (x * SpotReticuleDistance);
    }

    /// <summary>
    /// One HudDef frame of the aim point (rules 3a to 3d), from the merc HUD's PostRender: with the pawn's weapon, the
    /// reticle flash alpha; then, unless bReticuleOff, the weapon is holstered or there is no player, the aim point
    /// through the view of the frame into canvas units. <paramref name="WorldToCanvas"/> is that projection (the
    /// drawn camera of the frame: PlayerCalcView's location and rotation with FovAngle, as the native builds its
    /// FCameraSceneNode); null for a point it cannot place (behind the view), which leaves the last value.
    /// </summary>
    [UcNative("ASHud::execHudDef (aim point helper)", Exe = 0x10af7cf0)]
    [PortOf("nat:SBase.SHud.HudDef", Spec = "spec/hud/merc-reticle.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10af7cf0: P.MyWeapon gate, the flash alpha, then bReticuleOff (+0x498 m2), P.WeaponLoaded (+0xab4) and PlayerOwner (+0x3e0) gates; SpotReticule (+0x6a0) = projected x, y and z = 0")]
    [Provenance(ProvenanceKind.Estimated, "A point behind the view keeps the last value (the native divides by a negative W and writes the mirrored point)")]
    public void UpdateSpotReticule(Func<FVector, (float X, float Y)?> WorldToCanvas)
    {
        ArgumentNullException.ThrowIfNull(WorldToCanvas);
        if (P is not { MyWeapon: not null } pawn)
        {
            return;
        }

        ReticuleFlashAlpha = UpdateReticuleFlash();
        if (bReticuleOff || pawn.WeaponLoaded == 0 || PlayerOwner is not SPlayerController pc)
        {
            return;
        }

        if (WorldToCanvas(SpotReticuleAimPoint(pawn, pc)) is { } at)
        {
            SpotReticule = new FVector(at.X, at.Y, 0f);
        }
    }

    /// <summary>
    /// The reticle flash alpha (rule 3a, dormant: no page reads it): 1 without bReticuleFlash; else with
    /// e = now - ReticuleFlashTime, 1 - e / ReticuleFlashOn while e is at most ReticuleFlashOn, then
    /// (e - ReticuleFlashOn) / ReticuleFlashOff, clamped to 0..1; past one whole period the period start moves on.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10af7d0d..0x10af7dbe: bReticuleFlash (+0x894 m0x10), ReticuleFlashOn/Off/Time (+0x7a4/+0x7a8/+0x7ac), ReticuleFlashAlpha (+0x460)")]
    private float UpdateReticuleFlash()
    {
        if (!bReticuleFlash)
        {
            return 1f;
        }

        float now = Level.TimeSeconds;
        float e = now - ReticuleFlashTime;
        float a = e <= ReticuleFlashOn ? 1f - (e / ReticuleFlashOn) : (e - ReticuleFlashOn) / ReticuleFlashOff;
        a = a >= 0f ? (a >= 1f ? 1f : a) : 0f;
        if (ReticuleFlashOn + ReticuleFlashOff < now - ReticuleFlashTime)
        {
            ReticuleFlashTime += ReticuleFlashOn + ReticuleFlashOff;
        }

        return a;
    }

    /// <summary>
    /// A world point in canvas units through a view at <paramref name="CameraLocation"/> looking along
    /// <paramref name="CameraRotation"/> with the horizontal field of view <paramref name="FovDegrees"/> of a 4:3
    /// frame, on a screen <paramref name="Aspect"/> wide per unit of height (wider screens add width, as the game's
    /// renderer draws them). The centre of the view is (320.5, 240.5), y grows downward. Null behind the view.
    /// </summary>
    [PortOf("nat:SBase.SHud.HudDef", Spec = "spec/hud/merc-reticle.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Reloaded's projection for wide screens is not decoded: the 4:3 frame's vertical angle is kept (the renderer's Hor+)")]
    public static (float X, float Y)? ProjectToCanvas(FVector CameraLocation, FRotator CameraRotation, float FovDegrees, float Aspect, FVector Point)
    {
        CameraRotation.GetAxes(out FVector x, out FVector y, out FVector z);
        FVector d = Point - CameraLocation;
        float forward = d | x;
        if (forward <= 0f)
        {
            return null;
        }

        float halfTan = SvmMath.Tan(FovDegrees * (MathF.PI / 360f));
        float ndcX = (d | y) / forward / (halfTan * Aspect / FovReferenceAspect);
        float ndcY = (d | z) / forward / (halfTan / FovReferenceAspect);
        return (((1f + ndcX) * SpotReticuleCanvasWidth * 0.5f) + SpotReticuleCentreOffset,
            ((1f - ndcY) * SpotReticuleCanvasHeight * 0.5f) + SpotReticuleCentreOffset);
    }

    /// <summary>A point on the view's axis lands half a canvas unit right of and below the canvas centre.</summary>
    [Provenance(ProvenanceKind.Measured, "SpotReticule (320.5, 240.5) whenever the aim is the view (sandbox capture of the merc standing, crouched, strafing, eight aims, 1280 x 720)")]
    public const float SpotReticuleCentreOffset = 0.5f;

    /// <summary>The frame FovAngle is measured across: 4 wide per 3 high.</summary>
    [Provenance(ProvenanceKind.Decoded, "FovAngle is the horizontal angle of the 4:3 picture (docs/RENDERING.md, RenderK.FovReferenceAspect)")]
    public const float FovReferenceAspect = 4f / 3f;
}
