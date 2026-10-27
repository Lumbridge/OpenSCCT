using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController camera network view: entering and stepping round the ring, the vision switches, the zoom
// on the server, and the wait and view states. Written from spec/equipment/camera-network-gadget.md and
// spec/devices/cam-network.md rules 2 to 5 (FreeCurrentCamera and ClampRotation are in SPlayerController.Devices.cs).
public partial class SPlayerController
{
    private static readonly Name CNAME_UseCamNetwork = new("UseCamNetwork");
    private static readonly Name CNAME_CamNetworkSetFovOnServer = new("CamNetworkSetFovOnServer");
    private static readonly ClassRef CREF_SGadCamNetwork = new("SBase.SGadCamNetwork");

    /// <summary>
    /// UseCamNetwork (client to server): frees the current camera; leaving clears the used object; otherwise, in a
    /// camera state, picks the next (2), previous (1) or first (0) camera, takes control when it is free (its vision
    /// from the goggles), plays its activation sound and enters the view.
    /// </summary>
    [Uc("SBase.SPlayerController.UseCamNetwork", Sha1 = "a24a52d5637e8716f56e442da4fa94bdc56dc295")]
    [PortOf("uc:SBase.SPlayerController:UseCamNetwork", Spec = "spec/devices/cam-network.md#ring-and-entry")]
    public void UseCamNetwork(int iPickCam = 0, bool bLeaveCamMode = false)
    {
        if (GadgetNet.ToServer(this, this, new CombatCall(CNAME_UseCamNetwork) { Int1 = iPickCam, Bool1 = bLeaveCamMode }))
        {
            return;
        }

        if (SBPawn is not { } pawn)
        {
            return;
        }

        FreeCurrentCamera();
        if (bLeaveCamMode)
        {
            if (pawn.UsedObject is SCamNetwork left)
            {
                PlaySound(left.DesactivationSound, ESoundSlot.SLOT_None, 1f, false, K.StickyGasSoundRadius);
            }

            pawn.UsedObject = null;
            return;
        }

        if (!(IsInState(GadgetStates.s_UseObject_CamNetwork) || IsInState(GadgetStates.s_UseObject_CamNetworkCrWaitForServer) ||
            IsInState(GadgetStates.s_UseObject_CamNetworkStWaitForServer)))
        {
            return;
        }

        var current = pawn.UsedObject as SCamNetwork;
        if (iPickCam == 2 && current?.NextCam is { } next)
        {
            pawn.UsedObject = next;
        }
        else if (iPickCam == 1 && current?.PrevCam is { } prev)
        {
            pawn.UsedObject = prev;
        }
        else if (iPickCam == 0)
        {
            var gadget = PawnInventory.FindGadgetType(pawn, CREF_SGadCamNetwork) as SGadCamNetwork;
            if (gadget is { FirstNetworkCam: null })
            {
                gadget.FirstNetworkCam = SCamNetwork.FindFirstCamera(XLevel);
            }

            pawn.UsedObject = gadget?.FirstNetworkCam;
        }

        if (pawn.UsedObject is not SCamNetwork cam)
        {
            return;
        }

        if (cam.aUsedByPawn is null)
        {
            cam.aUsedByPawn = pawn;
            int mode = Goggle?.CurrentMode ?? SGoggle.REN_DynLight;
            if (mode == SGoggle.REN_MotionTrackingVision)
            {
                PawnGadgets.CamNetworkSwitchVisionModeOnServer(pawn, cam, 3);
            }

            PawnGadgets.CamNetworkSwitchVisionModeOnServer(pawn, cam, mode == SGoggle.REN_EMFVision ? 4 : -2);
        }

        PlaySound(cam.ActivationSound, ESoundSlot.SLOT_None, 1f, false, K.StickyGasSoundRadius);
        if (pawn.Health > 0 && GetStateName() != GadgetStates.s_UseObject_CamNetwork)
        {
            eventMirrorState(GadgetStates.s_UseObject_CamNetwork, Name.None, true);
        }
    }

    /// <summary>
    /// ChangeCamNetworkVision (rule 4): the goggles' mode for the camera view: 1 motion and 2 EMF (mercs only), 3 the
    /// cycle; the same mode again goes back to normal; then the mode takes effect.
    /// </summary>
    [Uc("SBase.SPlayerController.ChangeCamNetworkVision", Sha1 = "59c4bfa5cddf001300097b952f524ae8cf0dea60")]
    [PortOf("uc:SBase.SPlayerController:ChangeCamNetworkVision", Spec = "spec/equipment/camera-network-gadget.md#rules")]
    public void ChangeCamNetworkVision(float i = 0f)
    {
        if (Goggle is not { } goggle)
        {
            return;
        }

        int old = goggle.CurrentMode;
        switch (i)
        {
            case 1f:
                if (SPawnType == PawnType.SPAWNOFFENSE)
                {
                    return;
                }

                goggle.CurrentMode = SGoggle.REN_MotionTrackingVision;
                break;
            case 2f:
                if (SPawnType == PawnType.SPAWNOFFENSE)
                {
                    return;
                }

                goggle.CurrentMode = SGoggle.REN_EMFVision;
                break;
            case 3f:
                goggle.CurrentMode = goggle.CurrentMode switch
                {
                    SGoggle.REN_MotionTrackingVision => SGoggle.REN_EMFVision,
                    SGoggle.REN_EMFVision => SGoggle.REN_DynLight,
                    _ => SGoggle.REN_MotionTrackingVision,
                };
                break;
        }

        if (goggle.CurrentMode == old)
        {
            goggle.CurrentMode = SGoggle.REN_DynLight;
        }

        goggle.Activate();
    }

    /// <summary>The controlling merc's camera FOV on the server (client to server).</summary>
    [Uc("SBase.SPlayerController.CamNetworkSetFovOnServer", Sha1 = "b222debbc8ad86f6f5f0c790731222650c48f250")]
    [PortOf("uc:SBase.SPlayerController:CamNetworkSetFovOnServer", Spec = "spec/equipment/camera-network-gadget.md#rules")]
    public void CamNetworkSetFovOnServer(SStickyCam? Cam, float val)
    {
        if (GadgetNet.ToServer(this, this, new CombatCall(CNAME_CamNetworkSetFovOnServer) { Actor1 = Cam, Float1 = val }))
        {
            return;
        }

        if (Cam is not null)
        {
            Cam.fVisionFOV = val;
        }
    }

    /// <summary>The camera's shot request (client to server).</summary>
    [Uc("SBase.SPlayerController.CamFire", Sha1 = "2faed35ba3e23d8cf54990977db133e7c1832baf")]
    [PortOf("uc:SBase.SPlayerController:CamFire", Spec = "spec/equipment/camera-network-gadget.md#rules")]
    public void CamFire(float F, SCamNetwork? Cam)
    {
        if (GadgetNet.ToServer(this, this, new CombatCall(CNAME_CamFire) { Actor1 = Cam, Float1 = F }))
        {
            return;
        }

        Cam?.ServerFire(F);
    }

    private static readonly Name CNAME_CamFire = new("CamFire");

    /// <summary>The controlling merc of the used camera, else null.</summary>
    private SCamNetwork? ControlledCamera() =>
        SBPawn is { UsedObject: SCamNetwork cam } pawn && ReferenceEquals(cam.aUsedByPawn, pawn) ? cam : null;

    /// <summary>The camera wait state's move: no jump, the camera view for a non-merc, a still move.</summary>
    internal void CamNetworkWaitMove(float DeltaTime)
    {
        bPressedJump = false;
        if (SBPawn is { SPawnType: not PawnType.SPAWNDEFENSE })
        {
            UpdateCamRotation(DeltaTime);
        }

        SendMove(DeltaTime, FVector.Zero);
    }

    /// <summary>Waiting crouched for the server's camera (extends the crouched 2-hand rest).</summary>
    [Uc("SBase.SPlayerController.s_UseObject_CamNetworkCrWaitForServer")]
    public class s_UseObject_CamNetworkCrWaitForServer : s_PlayerWalkingCr2H
    {
        public static new readonly s_UseObject_CamNetworkCrWaitForServer Instance =
            new(UcMeta.s_UseObject_CamNetworkCrWaitForServer, s_PlayerWalkingCr2H.Instance);

        protected s_UseObject_CamNetworkCrWaitForServer(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_UseObject_CamNetworkCrWaitForServer.PlayerMove", Sha1 = "b82f0abc61b21359a8650e823c62db11224fef3f")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetworkCrWaitForServer.PlayerMove", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => self.CamNetworkWaitMove(DeltaTime);
    }

    /// <summary>Waiting standing for the server's camera (extends the standing 2-hand rest).</summary>
    [Uc("SBase.SPlayerController.s_UseObject_CamNetworkStWaitForServer")]
    public class s_UseObject_CamNetworkStWaitForServer : s_PlayerWalkingSt2H
    {
        public static new readonly s_UseObject_CamNetworkStWaitForServer Instance =
            new(UcMeta.s_UseObject_CamNetworkStWaitForServer, s_PlayerWalkingSt2H.Instance);

        protected s_UseObject_CamNetworkStWaitForServer(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_UseObject_CamNetworkStWaitForServer.PlayerMove", Sha1 = "c1057a9cd94983c3f6d6c7176fb6771526307097")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetworkStWaitForServer.PlayerMove", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => self.CamNetworkWaitMove(DeltaTime);
    }

    /// <summary>The camera network view (rule 3): steer, zoom, switch vision and step round the ring as controller.</summary>
    [Uc("SBase.SPlayerController.s_UseObject_CamNetwork")]
    public class s_UseObject_CamNetwork : s_UseObject_StickyCam, ICanBeGrabbed, IGadLaserSwitch, IGadTorcheSwitch, ISwitchHeadset,
        IClientChaffed
    {
        public static new readonly s_UseObject_CamNetwork Instance = new(UcMeta.s_UseObject_CamNetwork, null);

        protected s_UseObject_CamNetwork(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_UseObject_CamNetwork.CanBeGrabbed", Sha1 = "9ca447bdebf24bc1fe879d251877cf611c4265e3")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetwork.CanBeGrabbed", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public bool CanBeGrabbed(SPlayerController self) => self.SBPawn?.SPawnType == PawnType.SPAWNDEFENSE;

        [Uc("SBase.SPlayerController.s_UseObject_CamNetwork.GadLaserSwitch", Sha1 = "801879864adaa5f43302fca2d4abd15cb1471860")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetwork.GadLaserSwitch", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public void GadLaserSwitch(SPlayerController self)
        {
            if (self.ControlledCamera() is not null)
            {
                self.GadLaserSwitch();
            }
        }

        /// <summary>The controller's torch key: the camera's vision reset, the goggles moved by their mode, the torch key.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_CamNetwork.GadTorcheSwitch", Sha1 = "f55f3a03d48f4e5a63ecfa20e1ccd11e95ff2f1a")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetwork.GadTorcheSwitch", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public void GadTorcheSwitch(SPlayerController self)
        {
            if (self.ControlledCamera() is not { } cam || self.SBPawn is not { } pawn)
            {
                return;
            }

            int vision = VisionOf(self);
            PawnGadgets.CamNetworkSwitchVisionModeOnServer(pawn, cam, -2);
            self.ChangeCamNetworkVision(vision);
            self.GadTorcheSwitch();
        }

        /// <summary>The controller's goggle keys switch the camera and the goggles (only modes the server allows).</summary>
        [Uc("SBase.SPlayerController.s_UseObject_CamNetwork.SwitchHeadset", Sha1 = "c0a09c5286d90cb4ad3f7e21de5579172563d618")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetwork.SwitchHeadset", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public void SwitchHeadset(SPlayerController self, int i)
        {
            if (self.ControlledCamera() is not { } cam || self.SBPawn is not { } pawn)
            {
                return;
            }

            GameReplicationInfo? gri = self.GameReplicationInfo;
            bool spy = self.SPawnType == PawnType.SPAWNOFFENSE;
            bool first = gri is null || (spy ? gri.bAvlb_SPY_NightVision : gri.bAvlb_MERC_MotiontrackingVision);
            bool second = gri is null || (spy ? gri.bAvlb_SPY_ThermalVision : gri.bAvlb_MERC_EMFVision);
            if ((i == 1 && !first) || (i == 2 && !second))
            {
                return;
            }

            bool switched = false;
            if (i == 3)
            {
                bool motion = gri is null || gri.bAvlb_MERC_MotiontrackingVision;
                bool emf = gri is null || gri.bAvlb_MERC_EMFVision;
                if (!motion && !emf)
                {
                    return;
                }

                int mode = self.Goggle?.CurrentMode ?? SGoggle.REN_DynLight;
                if (!motion && mode == SGoggle.REN_DynLight)
                {
                    switched = true;
                    PawnGadgets.CamNetworkSwitchVisionModeOnServer(pawn, cam, 2);
                    self.ChangeCamNetworkVision(2f);
                }

                if (!emf && mode == SGoggle.REN_MotionTrackingVision)
                {
                    switched = true;
                    PawnGadgets.CamNetworkSwitchVisionModeOnServer(pawn, cam, 1);
                    self.ChangeCamNetworkVision(1f);
                }
            }

            if (pawn is SPAWNDEFENSE { Torche: { bInUse: true } torch })
            {
                torch.Unload();
            }

            if (!switched)
            {
                PawnGadgets.CamNetworkSwitchVisionModeOnServer(pawn, cam, i);
                self.ChangeCamNetworkVision(i);
            }
        }

        /// <summary>
        /// Grabs allowed for a merc; behind view; on the input machine the palm, the lights off, the camera action and
        /// the goggles saved and reset; the controlling merc's camera takes his FOV; the camera HUD.
        /// </summary>
        [Uc("SBase.SPlayerController.s_UseObject_CamNetwork.BeginState", Sha1 = "52e4a45c0f29fef905774a180232d76353ef7925")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetwork.BeginState", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn.SPawnType != PawnType.SPAWNOFFENSE)
            {
                pawn.bCanBeGrabbed = true;
            }

            pc.bBehindView = true;
            if (pc.IAmOnClient())
            {
                pawn.ChangeEnumAction(pawn.Physics == EPhysics.PHYS_Walking ? SBasePawn.SA_PALM_USING : SBasePawn.SA_NO_IK);
                if (pawn is SPAWNDEFENSE { TorcheLaser: { bInUse: true } laser })
                {
                    laser.Unload();
                }

                (PawnInventory.FindGadgetType(pawn, CREF_SGadTorche) as SGadTorche)?.Unload();
                pawn.ChangeEnumAction(SBasePawn.SA_CAMNETWORK_BG);
                if (pc.Goggle is { } goggle)
                {
                    goggle.SaveMode = goggle.CurrentMode;
                    goggle.ResetVisionMode();
                }
            }

            if (pc.ControlledCamera() is { } cam)
            {
                cam.fVisionFOV = pc.DesiredFOV;
                pc.ScopeSetFOV(cam.fVisionFOV);
            }

            GadgetHud.SetHudState(pc, GadgetStates.s_Hud_UseObject_CamNetwork);
        }

        /// <summary>
        /// The goggles' saved mode back (alive and not shocked), the camera left, the camera action ended, the
        /// camera's sounds and vision request settled; a dead merc's camera freed on the server; then the view's end.
        /// </summary>
        [Uc("SBase.SPlayerController.s_UseObject_CamNetwork.EndState", Sha1 = "d0adcc5cefb1c2430f861ab36daf0e22537d8824")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetwork.EndState", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            SBasePawn? pawn = pc.SBPawn;
            if (pawn is not null && pawn.SPawnType != PawnType.SPAWNOFFENSE)
            {
                pawn.bCanBeGrabbed = false;
            }

            if (pc.IAmOnClient())
            {
                if (pawn is { Health: > 0, bStickyShocking: false } && pc.Goggle is { } goggle)
                {
                    goggle.CurrentMode = SGoggle.REN_DynLight;
                    goggle.SwitchVisionMode(goggle.SaveMode);
                    goggle.CurrentMode = goggle.SaveMode;
                    goggle.Activate();
                }

                pc.UseCamNetwork(0, true);
                pawn?.ChangeEnumAction(SBasePawn.SA_CAMNETWORK_END);
                if (pawn?.UsedObject is SCamNetwork cam)
                {
                    cam.bPlayZoomOutSound = false;
                    cam.bPlayZoomInSound = false;
                    cam.iChangeToThisVisionMode = cam.iCurrentVisionMode;
                }

                if (pawn is not null)
                {
                    pawn.PreviousUsedObject = null;
                }

                pc.AmbientSound = AssetRef.None;
            }
            else if (pc.Role == ENetRole.ROLE_Authority && pawn is { Health: <= 0 })
            {
                pc.FreeCurrentCamera();
            }

            pc.bBehindView = false;
            base.EndState(self);
        }

        /// <summary>Chaff throws the merc out of the view (and forgets the saved goggle mode).</summary>
        [Uc("SBase.SPlayerController.s_UseObject_CamNetwork.ClientChaffed", Sha1 = "924101da00ae0269a8ab618ed9d5df8eac87dfac")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetwork.ClientChaffed", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public void ClientChaffed(SPlayerController self)
        {
            if (self.Goggle is { } goggle)
            {
                goggle.SaveMode = SGoggle.REN_DynLight;
            }

            Leave(self);
        }

        /// <summary>Alt-fire leaves the view.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_CamNetwork.AltFire", Sha1 = "ea77da91b1c4f7c69c800f2ac0a227a964968a55")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetwork.AltFire", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public override void AltFire(SPlayerController self, float F) => Leave(self);

        private static void Leave(SPlayerController self)
        {
            if (self.SBPawn is { } pawn && (pawn.Physics is EPhysics.PHYS_OnPipeH or EPhysics.PHYS_Vandamme || pawn.BTW != BTW_.NOBTW))
            {
                self.eventChangeState(self.PreviousState, Name.None, true);
            }
            else
            {
                self.GotoWaitingState();
            }
        }

        [Uc("SBase.SPlayerController.s_UseObject_CamNetwork.WheelUp", Sha1 = "a10443b1976cf14ce390d46fa3ed6952dd024607")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetwork.WheelUp", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public override void WheelUp(SPlayerController self)
        {
            if (self.ControlledCamera() is not null)
            {
                self.StickyCamZoom(-K.CamZoomWheelRate * self.Level.m_dT, true);
            }
        }

        [Uc("SBase.SPlayerController.s_UseObject_CamNetwork.WheelDown", Sha1 = "08915549bd72437ee85a9777c055b47447977d21")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetwork.WheelDown", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public override void WheelDown(SPlayerController self)
        {
            if (self.ControlledCamera() is not null)
            {
                self.StickyCamZoom(K.CamZoomWheelRate * self.Level.m_dT, true);
            }
        }

        /// <summary>The goggles' camera vision mode: 1 motion tracking, 2 EMF, else 0.</summary>
        private static int VisionOf(SPlayerController self) => self.Goggle?.CurrentMode switch
        {
            SGoggle.REN_MotionTrackingVision => 1,
            SGoggle.REN_EMFVision => 2,
            _ => 0,
        };

        /// <summary>
        /// The controller zooms, switches vision and steps round the ring with Use (next) and back-to-wall (previous),
        /// keeps the camera FOV and turns; a passenger follows the controller's vision and FOV; a still move.
        /// </summary>
        [Uc("SBase.SPlayerController.s_UseObject_CamNetwork.PlayerMove", Sha1 = "1470de125cfa02a05a97e7d84099e2b3ab9760cd")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_CamNetwork.PlayerMove", Spec = "spec/equipment/camera-network-gadget.md#rules")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bFire = 0;
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            if (self.bZoomIn > 0)
            {
                if (self.ControlledCamera() is not null)
                {
                    self.StickyCamZoom(-K.CamZoomKeyRate * self.Level.m_dT, true);
                }
            }
            else if (self.bZoomOut > 0 && self.ControlledCamera() is not null)
            {
                self.StickyCamZoom(K.CamZoomKeyRate * self.Level.m_dT, true);
            }

            if ((self.bUsing > 0 && self.oldbUsing != self.bUsing) || (self.bUseBackToWall > 0 && self.bUseBackToWall != self.oldbUseBackToWall))
            {
                int vision = VisionOf(self);
                if (vision != 0)
                {
                    if (self.ControlledCamera() is { } controlled)
                    {
                        PawnGadgets.CamNetworkSwitchVisionModeOnServer(pawn, controlled, vision);
                    }

                    self.ChangeCamNetworkVision(vision);
                }

                if (pawn is SPAWNDEFENSE { TorcheLaser: { bInUse: true } laser })
                {
                    laser.Unload();
                }

                (PawnInventory.FindGadgetType(pawn, CREF_SGadTorche) as SGadTorche)?.Unload();
                self.UseCamNetwork(self.bUseBackToWall > 0 ? 1 : 2);
            }

            self.oldbUsing = self.bUsing;
            self.fDateFireMode = self.bFireMode;
            self.oldbUseBackToWall = self.bUseBackToWall;
            if (pawn.UsedObject is SCamNetwork cam)
            {
                if (ReferenceEquals(cam.aUsedByPawn, pawn))
                {
                    if (!ReferenceEquals(pawn.PreviousUsedObject, cam))
                    {
                        cam.fVisionFOV = self.DefaultFOV;
                    }

                    if (cam.fVisionFOV != self.DesiredFOV)
                    {
                        self.ScopeSetFOV(cam.fVisionFOV);
                        self.CamNetworkSetFovOnServer(cam, cam.fVisionFOV);
                    }

                    self.eventUpdateRotation(DeltaTime, 0f);
                }
                else if (cam.aUsedByPawn is not null)
                {
                    if (cam.iChangeToThisVisionMode != -1)
                    {
                        self.ChangeCamNetworkVision(cam.iChangeToThisVisionMode);
                        cam.iChangeToThisVisionMode = -1;
                    }

                    int mode = self.Goggle?.CurrentMode ?? SGoggle.REN_DynLight;
                    if (!ReferenceEquals(pawn.PreviousUsedObject, cam) &&
                        ((cam.iCurrentVisionMode == 1 && mode != SGoggle.REN_MotionTrackingVision) ||
                         (cam.iCurrentVisionMode == 2 && mode != SGoggle.REN_EMFVision)))
                    {
                        self.ChangeCamNetworkVision(cam.iCurrentVisionMode);
                    }

                    if (cam.fVisionFOV != self.DesiredFOV)
                    {
                        self.ScopeSetFOV(cam.fVisionFOV);
                    }
                }

                self.AmbientSound = cam.AmbientSound;
            }

            pawn.PreviousUsedObject = pawn.UsedObject;
            self.SendMove(DeltaTime, FVector.Zero);
        }
    }
}
