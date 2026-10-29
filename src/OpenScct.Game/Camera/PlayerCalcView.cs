using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Camera;

/// <summary>
/// Where the view comes from each frame (spec/native/camera.md rules 1, 2, 4 and 5): the controller's calc view
/// picks a view source by state, the camera solves the following camera or the first-person view, and the result
/// lands in vCamLocation and rCamRotation. Run it on the machine that draws, after the tick, through
/// <see cref="FrameView"/> (the original solves it three times a frame, twice while the HUD is in a state).
/// </summary>
public static class PlayerCalcView
{
    private static readonly Name NAME_s_UseObject_Mirador = new("s_UseObject_Mirador");
    private static readonly Name NAME_s_UseObject_StickyCam_SmoothToPosition = new("s_UseObject_StickyCam_SmoothToPosition");
    private static readonly Name NAME_s_UseObject_StickyCam = new("s_UseObject_StickyCam");
    private static readonly Name NAME_s_UseObject_CamNetwork = new("s_UseObject_CamNetwork");
    private static readonly Name NAME_s_MultiSpectating = new("s_MultiSpectating");
    private static readonly Name NAME_s_Spectating_Visitor = new("s_Spectating_Visitor");
    private static readonly Name NAME_s_MultiSpectating_WhenKilled = new("s_MultiSpectating_WhenKilled");
    private static readonly Name NAME_s_PlayerFalling_OutOfWorld = new("s_PlayerFalling_OutOfWorld");
    private static readonly Name NAME_s_IntroCinematic = new("s_IntroCinematic");

    /// <summary>The view source of a controller state.</summary>
    public enum EViewSource
    {
        Default,
        Mirador,
        StickyCamSmoothToPosition,
        StickyCam,
        Spectating,
        OutOfWorld,
    }

    /// <summary>
    /// How many times the original's engine solves the local player's view (NativePlayerCalcView with bUpdate) in
    /// one rendered frame, each with that frame's full m_dT. Every ease of the following camera (the zoom into the
    /// armed view, the rise after a roll, the offset and target height) so moves this many steps per frame.
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "Frida count on the running original (SCCT Versus, Play Level SteeE, 2026-10-29): in 5 s, 1200 scene draws (0x109ebb20), 3600 NativePlayerCalcView calls (0x10aafe70) all with bUpdate 1 from 0x109ebbb9, 0x109e3f35 and 0x10af8e07 (1200 each), and 3600 follow-solver runs (0x10a94e00)")]
    public const int ViewSolvesPerFrame = 3;

    /// <summary>
    /// The view solves of a frame while the HUD is in one of its states (sticky cam, camera network, binoculars): their
    /// PostRender never reaches SHud's own one (super.PostRender in a state is HUD.PostRender), so the third solve, in
    /// the Enhanced Reality markers drawn from HudAtt / HudDef (0x10af8e07), does not happen.
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "Original sticky cam fly-in sampled by memory (TestRange, 2026-10-29, ~240 fps): 4000 uu/s steady = 2 x 2000 x m_dT per frame, 2640 uu/s (x0.66) for the first 0.145 s = Reloaded's timer drained by 2 calls a frame")]
    public const int HudStateViewSolvesPerFrame = 2;

    /// <summary>
    /// The view of one drawn frame as the original's engine makes it: <see cref="ViewSolvesPerFrame"/> calc views with
    /// bUpdate in a row (<see cref="HudStateViewSolvesPerFrame"/> while the HUD is in a state); the last one is the view
    /// drawn. The spy lasers' view update still runs once per tick (its own Level.TimeSeconds guard).
    /// </summary>
    [PortOf("eng:ASPlayerController::NativePlayerCalcView", Spec = "spec/native/camera.md#1-calc-view-dispatch")]
    public static void FrameView(SPlayerController PC, out Actor? ViewActor, out FVector CameraLocation, out FRotator CameraRotation)
    {
        ViewActor = null;
        CameraLocation = default;
        CameraRotation = default;
        int solves = PC.myHUD is SHud { StateNode: var hudState } && !ReferenceEquals(hudState, ActorState.None)
            ? HudStateViewSolvesPerFrame
            : ViewSolvesPerFrame;
        for (int i = 0; i < solves; i++)
        {
            NativeGlobalPlayerCalcView(PC, out ViewActor, out CameraLocation, out CameraRotation);
        }
    }

    /// <summary>NativeGlobalPlayerCalcView: the controller's calc view with bUpdate.</summary>
    [UcNative("ASPlayerController::execNativeGlobalPlayerCalcView", Editor = 0x111e74c0)]
    [PortOf("nat:SBase.SPlayerController.NativeGlobalPlayerCalcView", Spec = "spec/native/camera.md#1-calc-view-dispatch")]
    public static void NativeGlobalPlayerCalcView(SPlayerController PC, out Actor? ViewActor, out FVector CameraLocation,
        out FRotator CameraRotation) =>
        NativePlayerCalcView(PC, true, out ViewActor, out CameraLocation, out CameraRotation);

    /// <summary>The view source the controller's state picks (rule 1.2).</summary>
    [PortOf("eng:ASPlayerController::NativePlayerCalcView", Spec = "spec/native/camera.md#1-calc-view-dispatch")]
    public static EViewSource SourceOf(SPlayerController PC)
    {
        Name state = PC.GetStateName();
        if (state == NAME_s_UseObject_Mirador)
        {
            return EViewSource.Mirador;
        }

        if (state == NAME_s_UseObject_StickyCam_SmoothToPosition)
        {
            return EViewSource.StickyCamSmoothToPosition;
        }

        if (state == NAME_s_UseObject_StickyCam || state == NAME_s_UseObject_CamNetwork)
        {
            return EViewSource.StickyCam;
        }

        if (state == NAME_s_MultiSpectating || state == NAME_s_Spectating_Visitor || state == NAME_s_MultiSpectating_WhenKilled)
        {
            return EViewSource.Spectating;
        }

        if (state == NAME_s_PlayerFalling_OutOfWorld && PC.Pawn is SPawnAttaque)
        {
            return EViewSource.OutOfWorld;
        }

        return EViewSource.Default;
    }

    /// <summary>
    /// NativePlayerCalcView: the view of the controller's state; the result is also kept in vCamLocation and
    /// rCamRotation. Without <paramref name="bUpdate"/> the cameras do not solve again.
    /// </summary>
    [UcNative("ASPlayerController::NativePlayerCalcView", Editor = 0x111f3e50)]
    [PortOf("eng:ASPlayerController::NativePlayerCalcView", Spec = "spec/native/camera.md#1-calc-view-dispatch")]
        public static void NativePlayerCalcView(SPlayerController PC, bool bUpdate, out Actor? ViewActor, out FVector CameraLocation,
        out FRotator CameraRotation)
    {
        // The cinematic free camera (Reloaded ToggleFreeCamera): the view is the camera's own spot while it flies.
        if (PC.FreeCameraFlying != 0)
        {
            ViewActor = PC;
            CameraLocation = PC.FreeCamLocation;
            CameraRotation = PC.FreeCamRotation;
            PC.vCamLocation = CameraLocation;
            PC.rCamRotation = CameraRotation;
            return;
        }

        switch (SourceOf(PC))
        {
            case EViewSource.Mirador:
                MiradorView(PC, bUpdate, out ViewActor, out CameraLocation, out CameraRotation);
                break;
            case EViewSource.StickyCamSmoothToPosition:
                SmoothToStickyView(PC, bUpdate, out ViewActor, out CameraLocation, out CameraRotation);
                break;
            case EViewSource.StickyCam:
                StickyCamView(PC, bUpdate, out ViewActor, out CameraLocation, out CameraRotation);
                break;
            case EViewSource.Spectating:
                SpectatingView(PC, bUpdate, out ViewActor, out CameraLocation, out CameraRotation);
                break;
            case EViewSource.OutOfWorld:
                ViewActor = PC.Pawn;
                CameraLocation = PC.vCamLocWhenFallingDead;
                CameraRotation = PC.Pawn is { } pawn ? (pawn.Location - CameraLocation).Rotation() : PC.Rotation;
                break;
            default:
                DefaultView(PC, bUpdate, out ViewActor, out CameraLocation, out CameraRotation);
                break;
        }

        PC.vCamLocation = CameraLocation;
        PC.rCamRotation = CameraRotation;
        if (bUpdate)
        {
            // Rule 1.3: the spy pistols' lasers follow the view (spec/equipment/spy-shocker.md).
            SBase.SWeaponAttaque.ViewUpdate(PC, CameraLocation);
        }
    }

    /// <summary>
    /// The default view (rule 2): the view target (reset to the pawn, or the controller, when the pawn or the view
    /// target is gone); for bBehindView without bFirstPersonWithPawn the following view when the camera is in
    /// s_Following, else the camera's own view: the merc's debug camera (mode 1) or, outside the intro cinematic, the
    /// first-person view. The camera keeps the result in m_PrevPosition.
    /// </summary>
    [UcNative("ASPlayerController::NativePlayerCalcView_Default", Exe = 0x10ab00e0, Editor = 0x111e3830)]
    [PortOf("eng:ASPlayerController::NativePlayerCalcView", Spec = "spec/native/camera.md#2-default-view-editor-0x111e3830")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ab00e0: view target reset without a pawn or view target; own view 0x10a98840: debug mode 1 for a merc shows the controller's spot, else first person outside s_IntroCinematic; behind view only in s_Following (0x10a96590)")]
    public static void DefaultView(SPlayerController PC, bool bUpdate, out Actor? ViewActor, out FVector CameraLocation,
        out FRotator CameraRotation)
    {
        if (PC.Pawn is not { bDeleteMe: false } || PC.ViewTarget is null || PC.ViewTarget.bDeleteMe)
        {
            PC.ViewTarget = PC.Pawn is { bDeleteMe: false } p ? p : PC;
        }

        Actor target = PC.ViewTarget;
        ViewActor = target;
        CameraLocation = target.Location;
        CameraRotation = PC.Rotation;
        SPlayerCam? camera = PlayerCamera.Of(PC);
        if (camera is null)
        {
            return;
        }

        float dt = PC.Level.m_dT;
        if (PC.bBehindView && !PC.bFirstPersonWithPawn)
        {
            if (camera.StateNode is SPlayerCam.IBehindView)
            {
                FollowingView(PC, camera, bUpdate, ref CameraLocation, ref CameraRotation);
            }
        }
        else if (camera.DebugCamMode == SPlayerCam.DCMFixed && PC.SPawnType == PawnType.SPAWNDEFENSE)
        {
            FollowingView(PC, camera, false, ref CameraLocation, ref CameraRotation);
        }
        else if (!camera.IsInState(NAME_s_IntroCinematic))
        {
            camera.FirstPersonView(PC, target, dt, out CameraLocation, out CameraRotation);
        }

        camera.m_PrevPosition = CameraLocation;
    }

    /// <summary>
    /// The camera's s_Following view (game 0x10a96590): the pawn is SBPawn, else SpecTarget; with bUpdate, outside a
    /// pause, the following camera solves on it; with a pawn the view is the controller's spot (where the solver put
    /// it) and its rotation, or the solved view when it just solved. A spy in debug camera mode 1 keeps the view given.
    /// </summary>
    [PortOf("eng:ASPlayerController::NativePlayerCalcView", Spec = "spec/native/camera.md#3-following-camera-splayercam-s_following-editor-0x111f6af0-and-the-solver-0x111f56a0")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a96590: pawn = SBPawn ?? SpecTarget, solve with bUpdate and no pause, m_rotatedSam cleared, view = controller Location and Rotation")]
    public static void FollowingView(SPlayerController PC, SPlayerCam camera, bool bUpdate, ref FVector CameraLocation, ref FRotator CameraRotation)
    {
        SBasePawn? pawn = PC.SBPawn is { bDeleteMe: false } own ? own : PC.SpecTarget;
        if (pawn is null || (camera.DebugCamMode == SPlayerCam.DCMFixed && PC.SPawnType == PawnType.SPAWNOFFENSE))
        {
            return;
        }

        if (bUpdate && !IsPaused(PC))
        {
            camera.SolveFollowing(PC, pawn, PC.Level.m_dT, out CameraLocation, out CameraRotation);
            camera.m_rotatedSam = false;
            return;
        }

        CameraLocation = PC.Location;
        CameraRotation = PC.Rotation;
    }

    /// <summary>
    /// Spectating (rule 4.1): with a view target, the view starts at it, behind view goes on and, when the camera is in
    /// s_Following, the following view runs; the camera keeps the result in m_PrevPosition.
    /// </summary>
    [UcNative("ASPlayerController::NativePlayerCalcView_MultiSpectating", Exe = 0x10ab08d0, Editor = 0x111e3990)]
    [PortOf("eng:ASPlayerController::NativePlayerCalcView", Spec = "spec/native/camera.md#4-object-and-spectating-views")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ab08d0")]
    public static void SpectatingView(SPlayerController PC, bool bUpdate, out Actor? ViewActor, out FVector CameraLocation,
        out FRotator CameraRotation)
    {
        ViewActor = PC.ViewTarget;
        CameraLocation = PC.Location;
        CameraRotation = PC.Rotation;
        if (PC.ViewTarget is not { } target)
        {
            ViewActor = PC;
            return;
        }

        CameraLocation = target.Location;
        PC.bBehindView = true;
        if (PlayerCamera.Of(PC) is { } camera)
        {
            if (camera.StateNode is SPlayerCam.IBehindView)
            {
                FollowingView(PC, camera, bUpdate, ref CameraLocation, ref CameraRotation);
            }

            camera.m_PrevPosition = CameraLocation;
        }
    }

    /// <summary>
    /// Sticky camera and camera network (rule 4.2): 3 uu along the forward axis of the object's mount rotation (the
    /// X of its pitch, yaw and roll constraints), 5 more along its current forward axis in the camera network state,
    /// with the object's rotation; without a used object, the default view.
    /// </summary>
    [UcNative("ASPlayerController::NativePlayerCalcView_StickyCam", Exe = 0x10ab04f0, Editor = 0x111e7600)]
    [PortOf("eng:ASPlayerController::NativePlayerCalcView", Spec = "spec/native/camera.md#4-object-and-spectating-views")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ab04f0: rotation (int(PitchConstraint.X), int(YawConstraint.X), int(RollConstraint.X)) for the 3 uu; +5 along Rotation in controller state s_UseObject_CamNetwork")]
    public static void StickyCamView(SPlayerController PC, bool bUpdate, out Actor? ViewActor, out FVector CameraLocation,
        out FRotator CameraRotation)
    {
        if (PC.SBPawn?.UsedObject is not { bDeleteMe: false } used)
        {
            DefaultView(PC, bUpdate, out ViewActor, out CameraLocation, out CameraRotation);
            return;
        }

        FRotator mount = used is SOrientableObject o
            ? new FRotator(SvmMath.Trunc(o.PitchConstraint.X), SvmMath.Trunc(o.YawConstraint.X), SvmMath.Trunc(o.RollConstraint.X))
            : used.Rotation;
        ViewActor = used;
        CameraLocation = used.Location + mount.Vector() * CameraK.StickyCamOffset;
        if (PC.GetStateName() == NAME_s_UseObject_CamNetwork)
        {
            CameraLocation += used.Rotation.Vector() * CameraK.CamNetworkExtraOffset;
        }

        CameraRotation = used.Rotation;
    }

    /// <summary>
    /// The mirador (rule 4.3): the used object's location plus its CameraOffset along the object's axes, with the
    /// object's rotation; without a used object, the default view.
    /// </summary>
    [UcNative("ASPlayerController::NativePlayerCalcView_Mirador", Exe = 0x10ab06f0, Editor = 0x111e7850)]
    [PortOf("eng:ASPlayerController::NativePlayerCalcView", Spec = "spec/native/camera.md#4-object-and-spectating-views")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ab06f0: the vector at +0x3a8 of the used object (SProjoFixe.CameraOffset) rotated by its Rotation")]
    public static void MiradorView(SPlayerController PC, bool bUpdate, out Actor? ViewActor, out FVector CameraLocation, out FRotator CameraRotation)
    {
        if (PC.SBPawn?.UsedObject is not { bDeleteMe: false } used)
        {
            DefaultView(PC, bUpdate, out ViewActor, out CameraLocation, out CameraRotation);
            return;
        }

        FVector offset = used is SProjoFixe projo ? projo.CameraOffset : default;
        ViewActor = used;
        CameraLocation = used.Location + CameraMath.Rotate(offset, used.Rotation);
        CameraRotation = used.Rotation;
    }

    /// <summary>
    /// Smooth to the sticky camera (rule 4.4): the view looks from the used object to the pawn while it moves at
    /// 2000 uu/s toward a point 30 uu along the controller's forward axis from the object; within 80 uu of the object
    /// the controller changes to s_UseObject_StickyCam.
    /// </summary>
    [UcNative("ASPlayerController::NativePlayerCalcView_StickyCamSmoothToPosition", Exe = 0x10ab01e0, Editor = 0x111f1400)]
    [PortOf("eng:ASPlayerController::NativePlayerCalcView", Spec = "spec/native/camera.md#4-object-and-spectating-views")]
    public static void SmoothToStickyView(SPlayerController PC, bool bUpdate, out Actor? ViewActor, out FVector CameraLocation, out FRotator CameraRotation)
    {
        SPlayerCam? camera = PlayerCamera.Of(PC);
        if (PC.SBPawn?.UsedObject is not { bDeleteMe: false } used || camera is null)
        {
            DefaultView(PC, bUpdate, out ViewActor, out CameraLocation, out CameraRotation);
            return;
        }

        FVector target = used.Location + PC.Rotation.Vector() * CameraK.SmoothStickyLead;
        ViewActor = used;
        float step = SvmMath.Min(FVector.Dist(camera.m_PrevPosition, target), CameraK.SmoothStickySpeed * PC.Level.m_dT);
        step *= SmoothStickySlowStart(PC, camera);
        CameraLocation = CameraMath.Approach(camera.m_PrevPosition, target, step);
        CameraRotation = PC.Pawn is { } pawn ? (pawn.Location - used.Location).Rotation() : used.Rotation;
        camera.m_PrevPosition = CameraLocation;

        // The arrival (decoded 0x111f1400): within 80 uu of the object the view state takes over (sticky-cam.md rule 10).
        if (FVector.Dist(CameraLocation, used.Location) < CameraK.SmoothStickyArrive)
        {
            PC.eventChangeState(NAME_s_UseObject_StickyCam, Name.None, true);
        }
    }

    /// <summary>
    /// Reloaded's fly-in slow start (camera.md rule 4.4), the scale of this call's step: a new fly-in (Level.TimeSeconds
    /// went back, or moved more than 4 x m_dT since the last fly-in call) sets a timer to 100; while it is above 0 each
    /// call drains it by 0.34 x the frame time in milliseconds and the step is x 0.66. The fly-in view is solved twice a
    /// frame, so the slow part lasts about 0.15 s.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded 0x1001a880 (timer set), 0x1001a950 / 0x1001a8f0 (drain and x0.66 on the capped step); the frame time global 0x1036e2bc is read here as Level.m_dT x 1000")]
    private static float SmoothStickySlowStart(SPlayerController PC, SPlayerCam camera)
    {
        float now = PC.Level.TimeSeconds;
        float dt = PC.Level.m_dT;
        if (camera.SmoothStickyLastTime > now || now - camera.SmoothStickyLastTime > dt * CameraK.SmoothStickySlowGap)
        {
            camera.SmoothStickySlowTimer = CameraK.SmoothStickySlowTimer;
        }

        camera.SmoothStickyLastTime = now;
        if (camera.SmoothStickySlowTimer == 0f)
        {
            return 1f;
        }

        camera.SmoothStickySlowTimer -= dt * 1000f * CameraK.SmoothStickySlowDrain;
        if (!(camera.SmoothStickySlowTimer > 0f))
        {
            camera.SmoothStickySlowTimer = 0f;
        }

        return CameraK.SmoothStickySlowScale;
    }

    private static bool IsPaused(SPlayerController PC) => PC.Level.Pauser is not null;
}
