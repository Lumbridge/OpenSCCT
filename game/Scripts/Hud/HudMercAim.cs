using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScctGame.Hud;

/// <summary>
/// The merc's aim point for the reticle (spec/hud/merc-reticle.md rules 3a to 3d): what the global SHud.PostRender does
/// before HudDef draws the merc page, once per HUD frame. P follows the view target; a merc's HUD that is not hidden,
/// outside the spectating states, runs HudDef's aim point helper (<see cref="SHud.UpdateSpotReticule"/>) through the
/// controller's calc view of the last tick (vCamLocation, rCamRotation, FovAngle), as the original projects the aim of a
/// frame through the camera of the same frame. The picture drawn between ticks turns with the camera (render blend and
/// the mouse look-ahead), so the reticle stays where the aim sits in the view, as in the original's per-frame view.
/// </summary>
public static class HudMercAim
{
    private static readonly Name NAME_s_MultiSpectating = new("s_MultiSpectating");
    private static readonly Name NAME_s_Spectating_Visitor = new("s_Spectating_Visitor");
    private static readonly Name NAME_s_MultiSpectating_WhenKilled = new("s_MultiSpectating_WhenKilled");

    /// <summary>
    /// One HUD frame for <paramref name="pc"/> on a screen of <paramref name="screen"/> pixels. True when HudDef's aim
    /// point ran (SpotReticule may still keep its last value: holstered, bReticuleOff).
    /// </summary>
    [PortOf("nat:SBase.SHud.HudDef", Spec = "spec/hud/merc-reticle.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SHud.PostRender: P = SBasePawn(PlayerOwner.ViewTarget); spectators return early; HudDef for SPAWNDEFENSE while !bHideHUD")]
    public static bool Update(SPlayerController? pc, Vector2 screen)
    {
        if (pc?.myHUD is not SHud hud || screen.X <= 0f || screen.Y <= 0f)
        {
            return false;
        }

        if (pc.ViewTarget is SBasePawn viewed)
        {
            hud.P = viewed;
        }

        Name state = pc.GetStateName();
        // The cinematic camera's Hide HUD and free camera hide the aim too (the view is not the merc's).
        if (hud.P is not { SPawnType: PawnType.SPAWNDEFENSE } || hud.bHideHUD || pc.bCinematicHideHud || pc.FreeCameraFlying != 0 ||
            state == NAME_s_MultiSpectating ||
            state == NAME_s_Spectating_Visitor || state == NAME_s_MultiSpectating_WhenKilled)
        {
            return false;
        }

        // PostRender's merc colour, which HudDef's target name writes enemies in.
        (hud.Hud_R, hud.Hud_G, hud.Hud_B) = SHud.MercHudColour;
        FVector camera = pc.vCamLocation;
        FRotator view = pc.rCamRotation;
        float fov = pc.FovAngle;
        float aspect = screen.X / screen.Y;
        hud.UpdateSpotReticule(p => SHud.ProjectToCanvas(camera, view, fov, aspect, p));
        return true;
    }
}
