using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController cinematic camera (Enhanced 4.0 with Reloaded): the FreeCam and HideHud binds ask the server,
// the server answers when the host turned the camera on, and the player's own machine flies a free camera or hides
// the HUD. Written from spec/hud/cinematic-camera.md.
public partial class SPlayerController
{
    /// <summary>Where the free camera is, while <see cref="FreeCameraFlying"/> is set (this machine only).</summary>
    public FVector FreeCamLocation;

    /// <summary>Which way the free camera looks.</summary>
    public FRotator FreeCamRotation;

    /// <summary>The free camera's speed before Shift or Alt, in unreal units per second (the wheel steps it).</summary>
    public float FreeCamSpeed = CinematicCameraK.FreeCamSpeed;

    /// <summary>The speed factor the held keys give this tick (Shift fast, Alt slow), set by the input layer.</summary>
    public float FreeCamSpeedFactor = 1f;

    /// <summary>The Reloaded hide-HUD switch (ToggleHideHud); the HUD hides while it or HUD.bHideHUD is set.</summary>
    public bool bCinematicHideHud;

    private FVector _freeCamSavedLocation;
    private FRotator _freeCamSavedRotation;
    private Actor? _freeCamSavedViewTarget;
    private bool _freeCamSavedBehindView;

    /// <summary>The FreeCam key: stops a flying camera here, else asks the server (with a console line when off).</summary>
    [Uc("SBase.SPlayerController.FreeCam", Sha1 = "1a0bf9d204a9d96187f5ceb71676e359c8bba165")]
    [PortOf("uc:SBase.SPlayerController:FreeCam", Spec = "spec/hud/cinematic-camera.md#rules")]
    public void FreeCam()
    {
        if (FreeCameraFlying != 0)
        {
            ToggleFreeCamera();
            return;
        }

        if (!CinematicCameraGranted())
        {
            GameHost.ClientCall(this, new GameCall(CNAME_ClientMessage) { Text = "Free look is not enabled on this server." });
            return;
        }

        ServerRequestFreeCam();
    }

    /// <summary>The HideHud key: asks the server (with a console line when the cinematic camera is off).</summary>
    [Uc("SBase.SPlayerController.HideHud", Sha1 = "92826171b1a461d5291e2ca620105a0295e91297")]
    [PortOf("uc:SBase.SPlayerController:HideHud", Spec = "spec/hud/cinematic-camera.md#rules")]
    public void HideHud()
    {
        if (!CinematicCameraGranted())
        {
            GameHost.ClientCall(this, new GameCall(CNAME_ClientMessage) { Text = "Hiding the hud is not enabled on this server." });
            return;
        }

        ServerRequestHideHud();
    }

    /// <summary>The game replication info says the host allows the cinematic camera.</summary>
    [Uc("SBase.SPlayerController.CinematicCameraGranted", Sha1 = "ada238e76ba2f259a7552e1b79225526aaf9e78e")]
    [PortOf("uc:SBase.SPlayerController:CinematicCameraGranted", Spec = "spec/hud/cinematic-camera.md#rules")]
    public bool CinematicCameraGranted() => GameReplicationInfo is { bAvlb_CinematicCamera: true };

    /// <summary>Client to server: may I fly the free camera? The server answers with ClientStartFreeCam.</summary>
    [Uc("SBase.SPlayerController.ServerRequestFreeCam", Sha1 = "f34e1b3dc4fc4d614ac93ac28b9c37e878b62d78")]
    [PortOf("uc:SBase.SPlayerController:ServerRequestFreeCam", Spec = "spec/hud/cinematic-camera.md#rules")]
    public void ServerRequestFreeCam()
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            GameHost.ServerCall(this, new GameCall(CNAME_ServerRequestFreeCam));
            return;
        }

        if (CinematicCameraGranted())
        {
            ClientStartFreeCam();
        }
    }

    /// <summary>Client to server: may I hide the HUD? The server answers with ClientToggleHideHud.</summary>
    [Uc("SBase.SPlayerController.ServerRequestHideHud", Sha1 = "2f2e8917085282f27051778686ecf3b3e7299f9e")]
    [PortOf("uc:SBase.SPlayerController:ServerRequestHideHud", Spec = "spec/hud/cinematic-camera.md#rules")]
    public void ServerRequestHideHud()
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            GameHost.ServerCall(this, new GameCall(CNAME_ServerRequestHideHud));
            return;
        }

        if (CinematicCameraGranted())
        {
            ClientToggleHideHud();
        }
    }

    /// <summary>Server to client: the free camera was granted; toggle it on the player's machine.</summary>
    [Uc("SBase.SPlayerController.ClientStartFreeCam", Sha1 = "33006ebd08e5857795bdcfab91422322626615ff")]
    [PortOf("uc:SBase.SPlayerController:ClientStartFreeCam", Spec = "spec/hud/cinematic-camera.md#rules")]
    public void ClientStartFreeCam()
    {
        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall(CNAME_ClientStartFreeCam));
            return;
        }

        ToggleFreeCamera();
    }

    /// <summary>Server to client: hiding the HUD was granted; toggle it on the player's machine.</summary>
    [Uc("SBase.SPlayerController.ClientToggleHideHud", Sha1 = "b4bd2a66cf07786d1409413d87582e415e31d4c0")]
    [PortOf("uc:SBase.SPlayerController:ClientToggleHideHud", Spec = "spec/hud/cinematic-camera.md#rules")]
    public void ClientToggleHideHud()
    {
        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall(CNAME_ClientToggleHideHud));
            return;
        }

        ToggleHideHud();
    }

    /// <summary>
    /// The Reloaded test both natives share: always offline, else the server's GRI must have Alternate Mode and the
    /// cinematic camera on; and the player needs a camera and a living pawn.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded 0x10022f00 and 0x10022e70: standalone, or GRI.bAlternateMode and bAvlb_CinematicCamera; camera and living pawn")]
    public bool CinematicCameraAllowed()
    {
        bool server = Level.NetMode == ENetMode.NM_Standalone ||
            GameReplicationInfo is { bAlternateMode: true, bAvlb_CinematicCamera: true };
        return server && m_camera is not null && Pawn is { bDeleteMe: false, Health: > 0 };
    }

    /// <summary>
    /// Native 1900 (Reloaded): starts the free camera where the view is now, or stops it and gives the view back.
    /// Starting saves the view, makes the controller the view target and, unless the player is a merc (team 1), turns
    /// behind view off; the pawn stays where it is. Stopping restores all of that.
    /// </summary>
    [PortOf("rl:SBase.SPlayerController.ToggleFreeCamera", Spec = "spec/native/reloaded-hooks.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded 0x10022f00: saves vCamLocation, ClientSetViewTarget to the free camera, ClientSetBehindView(false) unless team 1, speeds 600; called again restores the saved view")]
    public void ToggleFreeCamera()
    {
        if (FreeCameraFlying != 0)
        {
            StopFreeCamera();
            return;
        }

        if (!CinematicCameraAllowed())
        {
            return;
        }

        _freeCamSavedLocation = vCamLocation;
        _freeCamSavedRotation = rCamRotation;
        _freeCamSavedViewTarget = ViewTarget;
        _freeCamSavedBehindView = bBehindView;
        FreeCamLocation = vCamLocation;
        FreeCamRotation = new FRotator(rCamRotation.Pitch, rCamRotation.Yaw, 0).Normalize();
        FreeCamSpeed = CinematicCameraK.FreeCamSpeed;
        FreeCameraFlying = 1;
        ViewTarget = this;
        if (PlayerReplicationInfo?.Team?.TeamIndex != 1)
        {
            bBehindView = false;
        }
    }

    /// <summary>Native 1901 (Reloaded): the same test as the free camera, then the hide-HUD switch flips.</summary>
    [PortOf("rl:SBase.SPlayerController.ToggleHideHud", Spec = "spec/native/reloaded-hooks.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded 0x10022e70: the ToggleFreeCamera permission test, then a Reloaded hide-HUD flag toggles")]
    public void ToggleHideHud()
    {
        if (CinematicCameraAllowed())
        {
            bCinematicHideHud = !bCinematicHideHud;
        }
    }

    /// <summary>Gives the view back to the pawn: the saved view target, behind view and camera spot.</summary>
    private void StopFreeCamera()
    {
        FreeCameraFlying = 0;
        ViewTarget = _freeCamSavedViewTarget is { bDeleteMe: false } target ? target : Pawn;
        bBehindView = _freeCamSavedBehindView;
        vCamLocation = _freeCamSavedLocation;
        rCamRotation = _freeCamSavedRotation;
        _freeCamSavedViewTarget = null;
    }

    /// <summary>
    /// Each local tick while the free camera flies, before PlayerTick: the mouse turns it and the movement keys fly it
    /// (Jump up, Crouch down), then the controller's input is cleared, so whatever state the controller is in the pawn
    /// stands still and does nothing. The camera stops when the pawn dies or goes.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Enhanced 4.0 key rows: movement keys, Space and Ctrl up and down, wheel sets speed, Shift fast, Alt slow; mouse turn uses the merc mouse rate; pitch +-16000; no collision")]
    public void TickFreeCamera(float DeltaTime)
    {
        if (FreeCameraFlying == 0)
        {
            return;
        }

        if (Pawn is not { bDeleteMe: false, Health: > 0 } || IsInState(NAME_s_Dead))
        {
            StopFreeCamera();
            return;
        }

        // The mouse axes the input layer stored (NativeComputeControlVariables copies them to fTurnValue unchanged).
        FRotator r = FreeCamRotation;
        r.Yaw += MovementMath.Int(aTurn * fSensibiliteSouris * DeltaTime * MovementK.RotationRateScale);
        r.Pitch += MovementMath.Int(aLookUp * fSensibiliteSouris * DeltaTime * MovementK.RotationRateScale);
        r = r.Normalize();
        r.Pitch = SvmMath.Clamp(r.Pitch, -CinematicCameraK.PitchLimit, CinematicCameraK.PitchLimit);
        r.Roll = 0;
        FreeCamRotation = r;

        float forward = BT_Up - BT_Down;
        float right = BT_Right - BT_Left;
        float up = (aUp > 0f ? 1f : 0f) - (bCrouch != 0 ? 1f : 0f);
        FVector side = new FRotator(0, r.Yaw + 16384, 0).Vector();
        FVector move = r.Vector() * forward + side * right + new FVector(0f, 0f, up);
        float speed = FreeCamSpeed * FreeCamSpeedFactor * DeltaTime;
        FreeCamLocation += move * speed;

        // The pawn keeps standing where it was.
        aForward = 0f;
        aStrafe = 0f;
        aUp = 0f;
        aTurn = 0f;
        aLookUp = 0f;
        fTurnValue = 0f;
        fLookUpValue = 0f;
        fNormePadMove = 0f;
        fNormePadCam = 0f;
        iMove = 0;
        BT_Up = BT_Down = BT_Left = BT_Right = 0;
        BT_PrevItem = BT_NextItem = 0;
        bCrouch = 0;
        bRoll = 0;
        bUsing = 0;
        bFireMode = 0;
        bUseBackToWall = 0;
        bReload = 0;
        bROF = 0;
        bHoldBreath = 0;
        bZoomIn = 0;
        bZoomOut = 0;
        bFire = 0;
        bAltFire = 0;
        bUsingNow = false;
        TimerUSe = 0f;
        fThrowForce = 0f;
    }

    /// <summary>The wheel steps the free camera's speed up or down (one step = x1.25), within limits.</summary>
    [Provenance(ProvenanceKind.Estimated, "Enhanced 4.0 key rows: the mouse wheel sets the free camera speed; step and limits ours")]
    public void StepFreeCamSpeed(int steps)
    {
        float s = FreeCamSpeed;
        for (int i = 0; i < SvmMath.Abs(steps); i++)
        {
            s = steps > 0 ? s * CinematicCameraK.SpeedStep : s / CinematicCameraK.SpeedStep;
        }

        FreeCamSpeed = SvmMath.Clamp(s, CinematicCameraK.MinSpeed, CinematicCameraK.MaxSpeed);
    }

    private static readonly Name CNAME_ClientMessage = new("ClientMessage");
    private static readonly Name CNAME_ServerRequestFreeCam = new("ServerRequestFreeCam");
    private static readonly Name CNAME_ServerRequestHideHud = new("ServerRequestHideHud");
    private static readonly Name CNAME_ClientStartFreeCam = new("ClientStartFreeCam");
    private static readonly Name CNAME_ClientToggleHideHud = new("ClientToggleHideHud");
}

/// <summary>The cinematic camera's numbers.</summary>
public static class CinematicCameraK
{
    /// <summary>The free camera's speed when it starts, in unreal units per second.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded 0x10022f00: two camera speeds set to 600")]
    public const float FreeCamSpeed = 600f;

    [Provenance(ProvenanceKind.Estimated, "Ours: Shift doubles the free camera speed")]
    public const float FastFactor = 2f;

    [Provenance(ProvenanceKind.Estimated, "Ours: Alt gives a quarter of the free camera speed")]
    public const float SlowFactor = 0.25f;

    [Provenance(ProvenanceKind.Estimated, "Ours: one wheel step")]
    public const float SpeedStep = 1.25f;

    [Provenance(ProvenanceKind.Estimated, "Ours")]
    public const float MinSpeed = 75f;

    [Provenance(ProvenanceKind.Estimated, "Ours")]
    public const float MaxSpeed = 4800f;

    /// <summary>How far the free camera looks up or down (rotator units; 16384 is straight up).</summary>
    [Provenance(ProvenanceKind.Estimated, "Ours: just short of straight up and down")]
    public const int PitchLimit = 16000;
}
