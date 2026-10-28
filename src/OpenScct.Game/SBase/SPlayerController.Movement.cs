using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController movement: the per-tick drive of PlayerTick, the state dispatch of the movement functions
// and the physics events, and the global versions of the movement functions. Written from spec/movement/*.md.
//
// Every function a movement state can override has a nested interface and an eventX dispatcher (the OpenScct.Core
// convention); the class-level version is the method with the UnrealScript name.
public partial class SPlayerController
{

    private bool _lastUseForControl;

    // ---- state dispatch -------------------------------------------------------------------------------------

    public interface IPlayerMove
    {
        void PlayerMove(SPlayerController self, float DeltaTime);
    }

    public interface IMyProcessMove
    {
        void MyProcessMove(SPlayerController self, float DeltaTime, FVector newAccel);
    }

    public interface IUpdateRotation
    {
        void UpdateRotation(SPlayerController self, float DeltaTime, float maxPitch);
    }

    public interface IMyFalling
    {
        void MyFalling(SPlayerController self, float LeftTime);
    }

    public interface IMyNotifyLanded
    {
        bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn);
    }

    public interface INotifyHitWall
    {
        bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall);
    }

    public interface INotifyBump
    {
        bool NotifyBump(SPlayerController self, Actor Other);
    }

    public interface IFellOutOfWorld
    {
        void FellOutOfWorld(SPlayerController self);
    }

    public interface IDropAttack
    {
        void DropAttack(SPlayerController self, SBasePawn PawnToAttack);
    }

    public interface IJump
    {
        void Jump(SPlayerController self, float F);
    }

    public interface ITopSynchro
    {
        void TopSynchro(SPlayerController self);
    }

    public interface ICheckArmsZone
    {
        void CheckArmsZone(SPlayerController self);
    }

    public interface ICheckFeetZone
    {
        void CheckFeetZone(SPlayerController self);
    }

    public interface IProcessWorldInteract
    {
        bool ProcessWorldInteract(SPlayerController self);
    }

    /// <summary>The back-to-wall key through the state (Mario walking enters back to wall).</summary>
    public bool eventProcessWorldInteract() =>
        StateNode is IProcessWorldInteract s ? s.ProcessWorldInteract(this) : ProcessWorldInteract();

    [Uc("SBase.SPlayerController.ProcessWorldInteract", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:ProcessWorldInteract", Spec = "spec/movement/back-to-wall.md")]
    public virtual bool ProcessWorldInteract() => false;

    public void eventPlayerMove(float DeltaTime)
    {
        if (StateNode is IPlayerMove s)
        {
            s.PlayerMove(this, DeltaTime);
        }
        else
        {
            PlayerMove(DeltaTime);
        }
    }

    public void eventMyProcessMove(float DeltaTime, FVector newAccel)
    {
        if (StateNode is IMyProcessMove s)
        {
            s.MyProcessMove(this, DeltaTime, newAccel);
        }
        else
        {
            MyProcessMove(DeltaTime, newAccel);
        }
    }

    public void eventUpdateRotation(float DeltaTime, float maxPitch)
    {
        if (StateNode is IUpdateRotation s)
        {
            s.UpdateRotation(this, DeltaTime, maxPitch);
        }
        else
        {
            UpdateRotation(DeltaTime, maxPitch);
        }
    }

    /// <summary>The walking physics lost the floor (IPhysicsEvents).</summary>
    public void eventMyFalling(float LeftTime)
    {
        if (StateNode is IMyFalling s)
        {
            s.MyFalling(this, LeftTime);
        }
        else
        {
            MyFalling(LeftTime);
        }
    }

    /// <summary>The falling physics touched a walkable floor (IPhysicsEvents).</summary>
    public bool eventMyNotifyLanded(FVector HitNormal, float fTimeLeft, Actor? LandedOn) =>
        StateNode is IMyNotifyLanded s ? s.MyNotifyLanded(this, HitNormal, fTimeLeft, LandedOn) : MyNotifyLanded(HitNormal, fTimeLeft, LandedOn);

    /// <summary>The pawn ran into a wall (IPhysicsEvents). False lets the native handle it.</summary>
    public bool eventNotifyHitWall(FVector HitNormal, Actor? Wall) =>
        StateNode is INotifyHitWall s && s.NotifyHitWall(this, HitNormal, Wall);

    /// <summary>The pawn touched a blocking actor (IPhysicsEvents). False lets the native handle it.</summary>
    public bool eventNotifyBump(Actor Other) => StateNode is INotifyBump s && s.NotifyBump(this, Other);

    /// <summary>The pawn passed below the world (IPhysicsEvents).</summary>
    public void eventFellOutOfWorld()
    {
        if (StateNode is IFellOutOfWorld s)
        {
            s.FellOutOfWorld(this);
        }
        else
        {
            FellOutOfWorld();
        }
    }

    /// <summary>The falling, rolling or zip physics came down on another pawn (IPhysicsEvents).</summary>
    public void eventDropAttack(Pawn Target)
    {
        if (Target is SBasePawn target && StateNode is IDropAttack s)
        {
            s.DropAttack(this, target);
        }
    }

    public void eventJump(float F = 0f)
    {
        if (StateNode is IJump s)
        {
            s.Jump(this, F);
        }
        else
        {
            Jump(F);
        }
    }

    /// <summary>A repositioning or a slide reached its target (native event; the global version is empty).</summary>
    public void eventTopSynchro()
    {
        if (StateNode is ITopSynchro s)
        {
            s.TopSynchro(this);
        }
    }

    /// <summary>CheckArmsZone through the state (a state can ignore or replace the native).</summary>
    public void eventCheckArmsZone()
    {
        if (StateNode is ICheckArmsZone s)
        {
            s.CheckArmsZone(this);
        }
        else
        {
            CheckArmsZone();
        }
    }

    /// <summary>CheckFeetZone through the state (a state can ignore or replace the native).</summary>
    public void eventCheckFeetZone()
    {
        if (StateNode is ICheckFeetZone s)
        {
            s.CheckFeetZone(this);
        }
        else
        {
            CheckFeetZone();
        }
    }

    // ---- per tick -------------------------------------------------------------------------------------------

    /// <summary>
    /// With the movement layer installed, a player controlled on this machine (a local viewport) runs PlayerTick
    /// (input, then the current state's PlayerMove) and sends the pawn's actions, before the rest of its tick. The
    /// pawn ticks after its controller (APawn's tick waits for its Owner and its Controller): its animation advances
    /// first (AnimEnd events), then its physics.
    /// </summary>
    [UcNative("ASPlayerController::Tick", Editor = 0x111f41b0)]
    [Provenance(ProvenanceKind.Estimated, "UE2 APlayerController::Tick calls PlayerTick for the viewport player; order measured from the goldens (move and physics in the same tick)")]
    [Provenance(ProvenanceKind.Decoded, "the pawn's tick check editor 0x11182490 defers a pawn until its Owner and Controller ticked; ASBasePawn::Tick 0x11205fb0 updates the animation before TickAuthoritative")]
    [Provenance(ProvenanceKind.Decoded, "ASPlayerController::Tick 0x111f41b0 ends with SendActions for a pawn with Role 3 and ServerUpdateActionCounts 0x111f8c80 for a pawn with Role 4")]
    protected override void NativeTick(float DeltaSeconds)
    {
        // The server keeps a downed player's chat partners first (SPlayerController.DownedChat.cs).
        if (Role == ENetRole.ROLE_Authority && SBPawn is { bLyingOnGround: true })
        {
            UpdateDownedChat();
        }

        if (Role < ENetRole.ROLE_Authority)
        {
            ApplyPendingVoiceChatMode();
        }

        bool movement = MovementService.Of(this) is not null;
        if (movement && Player is UViewport)
        {
            // The cinematic free camera takes this tick's input before any state sees it, so the pawn gets none
            // (spec/hud/cinematic-camera.md).
            TickFreeCamera(DeltaSeconds);
            eventPlayerTick(DeltaSeconds);
            if (SBPawn is { Role: not ENetRole.ROLE_Authority } own)
            {
                own.SendActions();
            }
        }

        base.NativeTick(DeltaSeconds);

        // The batch the pawn played this tick (here or received from its owner) is stamped for the other viewers.
        if (movement && !bDeleteMe && SBPawn is { Role: ENetRole.ROLE_Authority, bDeleteMe: false } pawn)
        {
            OpenScct.Game.Animation.ProxyAnimation.ServerUpdateActionCounts(pawn);
        }
    }

    // ---- input ----------------------------------------------------------------------------------------------

    /// <summary>The input and control part of the global PlayerTick (input-and-rotation.md, frame order).</summary>
    /// <remarks>
    /// The free camera's input clears run in TickFreeCamera, before PlayerTick (the cinematic camera takes the
    /// movement input too). Not ported here: the console input clears (front end), the heartbeat sound stop, the gadget,
    /// smoke and HUD parts, ClientUpdatePosition (netcode) and the objective idle timer (objectives spec). The specials
    /// chat (the Hack Comm key) follows the move as in the original, then the camera goes to the server every 0.14 s.
    /// </remarks>
    [Uc("SBase.SPlayerController.PlayerTick", Sha1 = "5e6178b93ec98230e270a689b61159665cd12ee7")]
    [PortOf("uc:SBase.SPlayerController:PlayerTick", Spec = "spec/movement/input-and-rotation.md#frame-order-global-playertick")]
    protected override void PlayerTick(float DeltaTime)
    {
        float now = Level.TimeSeconds;
        if (Level.NetMode == ENetMode.NM_Client && GameReplicationInfo is { } gri)
        {
            gri.bAvlb_CinematicCamera = bAvlb_CinematicCamera;
        }

        if (fTimeWhenHit > 0f && fTimeWhenHit + AdrenalineTime < now)
        {
            fTimeWhenHit = MovementK.AdrenalineReset;
        }

        NativeComputeControlVariables(DeltaTime);
        if (SBPawn is not { } pawn)
        {
            return;
        }

        if (bJammered)
        {
            if (now > fTimeToStopJammer)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_JAMMER_STOP);
                bJammered = false;
            }
            else
            {
                float k = SvmMath.Max(MovementK.JammerMinScale, 1f - (fTimeToStopJammer - now) / fJammerDuration);
                aForward *= k;
                aStrafe *= k;
                bPressedJump = false;
            }
        }

        if (iDontUpdatePadInput != 0)
        {
            if (now > iTimeToStopStickyShock)
            {
                iDontUpdatePadInput = 0;
                pawn.ChangeEnumAction(SBasePawn.SA_DMG_ELECTRIC_STOP_ANIM);
                if (bTazered)
                {
                    bTazered = false;
                    if (IsInState(MovementStateNames.s_PlayerWalkingSt2H) || IsInState(MovementStateNames.s_PlayerWalkingSt0H) ||
                        IsInState(MovementStateNames.s_PlayerWalkingCr0H) || IsInState(MovementStateNames.s_PlayerWalkingCr2H))
                    {
                        eventChangeState(MovementStateNames.s_FallDown, MovementStateNames.DIE_InPlace);
                    }
                }
            }
        }
        else
        {
            ComputeCrouchOrRoll();
        }

        FireModeButton(pawn, now);
        UnCrouchAndUnDraw(pawn);
        eventPlayerMove(DeltaTime);
        UpdateSpecialsChat();
        if (now - lastCameraReplicationPositionTime > CameraReplicationInterval)
        {
            lastCameraReplicationPositionTime = now;
            UpdatePositionForServer(Location, Rotation);
        }
    }

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.PlayerTick: UpdatePositionForServer when TimeSeconds - lastCameraReplicationPositionTime > 0.14")]
    private const float CameraReplicationInterval = 0.14f;

    /// <summary>
    /// Client to server (unreliable): the controller's spot and rotation, which the camera solver keeps at the view
    /// (spec/native/camera.md). The server's copy takes them, so its relevancy pass sees from the player's camera
    /// (spec/native/net-relevancy.md); without it the server's controller stayed where its pawn was possessed, and
    /// switches, ammo stocks and infirmaries more than 2500 uu from the spawn never reached the client.
    /// </summary>
    [Uc("SBase.SPlayerController.UpdatePositionForServer", Sha1 = "d8bf78d23adf6705656c33139ddb952bc3bade24")]
    [PortOf("uc:SBase.SPlayerController:UpdatePositionForServer", Spec = "spec/net/move-protocol.md#rules")]
    [PortOf("rep:SBase.SPlayerController.UpdatePositionForServer", Spec = "spec/net/move-protocol.md#rules")]
    public void UpdatePositionForServer(FVector NewLocation, FRotator NewRotation)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            MovementNet.Of(this).UpdatePositionForServer(this, NewLocation, NewRotation);
            return;
        }

        SetLocation(NewLocation);
        Rotation = NewRotation;
    }

    /// <summary>
    /// The spy's fire-mode button (spy-shocker.md rule 1): holstered, a press after a release starts a 0.5 s window;
    /// holding past it asks for the weapon without the laser, releasing inside it asks for it with the laser. Armed, a
    /// release arms bTestBoutonLache, which the armed states read to holster on the next press (and clear), so the
    /// press that holsters never draws again when it is let go.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:PlayerTick", Spec = "spec/equipment/spy-shocker.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.PlayerTick spy fire-mode block (fDateFireMode = now + 0.5, bTestBoutonLache)")]
    private void FireModeButton(SBasePawn pawn, float now)
    {
        if (pawn is SPAWNDEFENSE)
        {
            return;
        }

        if (pawn.WeaponLoaded == 0)
        {
            if (bFireMode > 0)
            {
                if (bTestBoutonLache)
                {
                    if (fDateFireMode == 0f)
                    {
                        fDateFireMode = now + FireModeHoldTime;
                    }
                    else if (now > fDateFireMode)
                    {
                        bForceFireMode = true;
                        bForceLaserFireMode = false;
                        bTestBoutonLache = false;
                        fDateFireMode = 0f;
                    }
                }
            }
            else
            {
                if (fDateFireMode > 0f)
                {
                    bForceFireMode = true;
                    bForceLaserFireMode = true;
                    bTestBoutonLache = false;
                }
                else
                {
                    bTestBoutonLache = true;
                }

                fDateFireMode = 0f;
            }
        }
        else
        {
            fDateFireMode = 0f;
            if (bFireMode == 0)
            {
                bTestBoutonLache = true;
            }
        }
    }

    [Provenance(ProvenanceKind.Decoded, "PlayerTick fire mode hold time 0.5 s")]
    private const float FireModeHoldTime = 0.5f;

    /// <summary>The spy's request to stand up and holster (input-and-rotation.md rule 9).</summary>
    private void UnCrouchAndUnDraw(SBasePawn pawn)
    {
        if (pawn is SPAWNDEFENSE)
        {
            return;
        }

        if (!eventCanUncrouchAndUndraw())
        {
            bUnCrouchAndUnDraw = 0;
            return;
        }

        if (bUnCrouchAndUnDraw == 0)
        {
            return;
        }

        if (pawn.bIsCrouched)
        {
            bCrouchWithRoll = false;
        }
        else if (pawn.WeaponLoaded > 0)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2Hto0H);
            eventChangeState(MovementStateNames.s_PlayerWalkingMario_T_2HToOH);
        }
        else
        {
            bUnCrouchAndUnDraw = 0;
            MovementService.HooksOf(this).LaunchQuickGadget(this);
        }
    }

    /// <summary>The input native (see <see cref="ControlInput"/>).</summary>
    [UcNative("ASPlayerController::execNativeComputeControlVariables", Exe = 0x10aaf400, Editor = 0x111f37d0)]
    [PortOf("nat:SBase.SPlayerController.NativeComputeControlVariables", Spec = "spec/movement/input-and-rotation.md#frame-order-global-playertick")]
    public void NativeComputeControlVariables(float dt) => ControlInput.Compute(this, dt, ref _lastUseForControl);

    /// <summary>
    /// The crouch key toggles the crouch latch once per press; the roll key asks for a roll when the spy is walking
    /// unarmed, pushing the stick at least 0.3, moving at 65 uu/s or more and past the roll cooldown.
    /// </summary>
    [Uc("SBase.SPlayerController.ComputeCrouchOrRoll", Sha1 = "1ff9a2a926a1a3e8631bbc9141e8004a88da6126")]
    [PortOf("uc:SBase.SPlayerController:ComputeCrouchOrRoll", Spec = "spec/movement/input-and-rotation.md#crouch-and-roll-latch-computecrouchorroll")]
    public virtual void ComputeCrouchOrRoll()
    {
        if (bCrouch > 0 && !bCanComputeRollButton)
        {
            bCanComputeRollButton = true;
            bCrouchWithRoll = !bCrouchWithRoll;
        }
        else if (bCrouch == 0)
        {
            bCanComputeRollButton = false;
        }

        if (bRoll > 0 && SBPawn is { } pawn && pawn is not SPAWNDEFENSE && pawn.Physics == EPhysics.PHYS_Walking &&
            pawn.WeaponLoaded == 0 && !(fNormePadMove < MovementK.RollMinStick) &&
            !(pawn.Velocity.Size() < MovementK.RollMinSpeed) && !(m_LastRollingTime >= Level.TimeSeconds - fTimeBetweenTwoRolls))
        {
            bRollAction = true;
        }
    }

    /// <summary>The spy's slow/fast toggle (middle mouse).</summary>
    [Uc("SBase.SPlayerController.ChangeSpeed", Sha1 = "96b7591155aab3386943f39401182bf69abf34d8")]
    [PortOf("uc:SBase.SPlayerController:ChangeSpeed", Spec = "spec/movement/input-and-rotation.md#speed-mode-getspeedmode-changespeed")]
    public virtual void ChangeSpeed()
    {
        if (SBPawn is SPawnAttaque)
        {
            bFastSpeed = !bFastSpeed;
        }
    }

    /// <summary>The factor the input native applies to the stick: 0.8, 1.0, 0.6 or 0.4.</summary>
    [Uc("SBase.SPlayerController.GetSpeedMode", Sha1 = "1c55aa7712cd3008064ae7653f661413f1c0d2ec")]
    [PortOf("uc:SBase.SPlayerController:GetSpeedMode", Spec = "spec/movement/input-and-rotation.md#speed-mode-getspeedmode-changespeed")]
    public virtual float GetSpeedMode()
    {
        if (bBinocularVision)
        {
            return MovementK.SpeedModeBinoculars;
        }

        if (SBPawn is null || SBPawn is SPAWNDEFENSE || bFastSpeed)
        {
            return MovementK.SpeedModeFull;
        }

        return IsInState(MovementStateNames.s_PlayerWalkingWeapon) ? MovementK.SpeedModeWeaponSlow : MovementK.SpeedModeSlow;
    }

    /// <summary>The Jump exec: asks the movement state for a jump, unless the inventory is open.</summary>
    [Uc("Engine.PlayerController.Jump", Sha1 = "da1efe3578026bda399ed85aee1263b9d2ffe764")]
    [PortOf("uc:Engine.PlayerController:Jump", Spec = "spec/movement/input-and-rotation.md#jump-request")]
    public virtual void Jump(float F = 0f)
    {
        if (!bIsInInventoryMode)
        {
            bPressedJump = true;
        }
    }

    // ---- moves ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The class-level PlayerMove. Its body (115 bytes) is not described by the movement specs, which only use the
    /// state versions; here it does nothing.
    /// </summary>
    public virtual void PlayerMove(float DeltaTime)
    {
    }

    /// <summary>Sets the pawn's acceleration for this move.</summary>
    [Uc("SBase.SPlayerController.MyProcessMove", Sha1 = "c5112b026c6433c1f4b69fd0b3a23cec7db0e685")]
    [PortOf("uc:SBase.SPlayerController:MyProcessMove", Spec = "spec/movement/walking.md#spy-mario-walking-s_playerwalkingmarioplayermove")]
    public virtual void MyProcessMove(float DeltaTime, FVector newAccel)
    {
        if (Pawn is { } pawn)
        {
            pawn.Acceleration = newAccel;
        }
    }

    /// <summary>The server runs a client's move through the current state (called by the move natives).</summary>
    [Uc("SBase.SPlayerController.ScriptMyProcessMove", Sha1 = "49ede3e43742f5d8ce8e357b7e951156fde1fdf6")]
    [PortOf("uc:SBase.SPlayerController:ScriptMyProcessMove", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public void ScriptMyProcessMove(float DeltaTime, FVector newAccel) => eventMyProcessMove(DeltaTime, newAccel);

    /// <summary>
    /// Sends this frame's move: a client saves, applies and sends it (MyReplicateMove, netcode); the server's own
    /// player runs it here (MyProcessMove).
    /// </summary>
    public void SendMove(float DeltaTime, FVector newAccel)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            MyReplicateMove(DeltaTime, newAccel);
        }
        else
        {
            eventMyProcessMove(DeltaTime, newAccel);
        }
    }

    /// <summary>The client move native (netcode spec); without a network layer the move is applied locally.</summary>
    [UcNative("ASPlayerController::execMyReplicateMove", Exe = 0x10aad5c0, Editor = 0x111f3000)]
    [PortOf("nat:SBase.SPlayerController.MyReplicateMove", Spec = "spec/movement/native-contracts.md#per-native-contracts")]
    public void MyReplicateMove(float DeltaTime, FVector newAccel) => MovementNet.Of(this).MyReplicateMove(this, DeltaTime, newAccel);

    /// <summary>The client flushes the move so far at a physics event (netcode spec).</summary>
    [UcNative("ASPlayerController::execMyReplicateMoveWithoutMoving", Exe = 0x10aae310, Editor = 0x111f3450)]
    [PortOf("nat:SBase.SPlayerController.MyReplicateMoveWithoutMoving", Spec = "spec/movement/native-contracts.md#per-native-contracts")]
    public void MyReplicateMoveWithoutMoving(float LeftTime, FVector newAccel) =>
        MovementNet.Of(this).MyReplicateMoveWithoutMoving(this, LeftTime, newAccel);

    // ---- crouch ---------------------------------------------------------------------------------------------

    public interface ICanCrouch
    {
        bool CanCrouch(SPlayerController self);
    }

    public bool eventCanCrouch() => StateNode is ICanCrouch s ? s.CanCrouch(this) : CanCrouch();

    [Uc("SBase.SPlayerController.CanCrouch", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanCrouch", Spec = "spec/movement/walking.md#crouch-requests-processcrouch")]
    public virtual bool CanCrouch() => false;

    /// <summary>
    /// Turns the crouch latch into a stance change: stand up when the latch is off and there is room, crouch when
    /// it is on, each through the transition state for the hands, at most once per 0.5 s.
    /// </summary>
    [Uc("SBase.SPlayerController.ProcessCrouch", Sha1 = "4a8836e6bf6f5e01b3fffca1460ded1254682581")]
    [PortOf("uc:SBase.SPlayerController:ProcessCrouch", Spec = "spec/movement/walking.md#crouch-requests-processcrouch")]
    public virtual void ProcessCrouch()
    {
        if (SBPawn is not { } pawn || pawn.Physics == EPhysics.PHYS_Falling || bTazered || !pawn.bCanCrouch)
        {
            return;
        }

        if (!eventCanCrouch())
        {
            bCrouchWithRoll = pawn.bIsCrouched;
            return;
        }

        float now = Level.TimeSeconds;
        bool merc = pawn is SPAWNDEFENSE;
        if (!bCrouchWithRoll && pawn.bIsCrouched)
        {
            if (pawn.CanUnCrouch() && m_LastSpecialMoveTime < now - MovementK.StanceCooldown)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
                m_LastSpecialMoveTime = now;
                bPressedJump = false;
                bFireMode = 0;
                bForceFireMode = false;
                if (pawn.WeaponLoaded == 0)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WAIT_CR2ST_0H);
                    eventChangeState(merc ? MovementStateNames.s_PlayerWalkingWeapon_T_CrToSt : MovementStateNames.s_PlayerWalkingMario_T_CrToSt);
                }
                else
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WAIT_CR2ST_2H);
                    eventChangeState(MovementStateNames.s_PlayerWalkingWeapon_T_CrToSt);
                }
            }
            else if (!pawn.CanUnCrouch())
            {
                bCrouchWithRoll = true;
            }
        }
        else if (bCrouchWithRoll && !pawn.bIsCrouched && m_LastSpecialMoveTime < now - MovementK.StanceCooldown)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
            bPressedJump = false;
            bFireMode = 0;
            bForceFireMode = false;
            m_LastSpecialMoveTime = now;
            if (pawn.WeaponLoaded == 0)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WAIT_ST2CR_0H);
                eventChangeState(merc ? MovementStateNames.s_PlayerWalkingWeapon_T_StToCr : MovementStateNames.s_PlayerWalkingMario_T_StToCr);
            }
            else
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WAIT_ST2CR_2H);
                eventChangeState(MovementStateNames.s_PlayerWalkingWeapon_T_StToCr);
            }
        }
    }

    /// <summary>In the air: stand up if crouched (forced) and keep the crouch wish for the landing.</summary>
    [Uc("SBase.SPlayerController.ZoneHorsCrouch", Sha1 = "42cb59d83b619a5e460a6dd5777491df4b454d0c")]
    [PortOf("uc:SBase.SPlayerController:ZoneHorsCrouch", Spec = "spec/movement/walking.md#crouch-requests-processcrouch")]
    public virtual void ZoneHorsCrouch()
    {
        if (SBPawn is { bIsCrouched: true } pawn)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH, true);
            bCrouchWithRoll = true;
        }
    }

    // ---- physics events (global versions) -------------------------------------------------------------------

    /// <summary>
    /// The floor was lost: the client flushes its move, then on the controlling side the fall action and the
    /// fall state; a slow spy with a ledge behind his feet and no floor below turns to hang from it.
    /// </summary>
    [Uc("SBase.SPlayerController.MyFalling", Sha1 = "3577625c4ce35f33455771f4ced910c343af7a5e")]
    [PortOf("uc:SBase.SPlayerController:MyFalling", Spec = "spec/movement/jump-fall-land.md#leaving-the-ground-without-a-jump-myfalling")]
    public virtual void MyFalling(float LeftTime)
    {
        if (Role < ENetRole.ROLE_Authority && SBPawn is { bActionsHasBeenSentByServerMove: false })
        {
            MyReplicateMoveWithoutMoving(LeftTime, Pawn?.Acceleration ?? default);
        }

        if (!IAmOnClient() || SBPawn is not { } pawn)
        {
            return;
        }

        if (pawn.BTW != BTW_.NOBTW)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_BTW_OUT);
        }

        if (pawn.GetAnimName(0) != MovementStateNames.LandStAlAk0)
        {
            pawn.ChangeEnumAction(pawn.WeaponLoaded == 0 ? SBasePawn.SA_FALL_ST_0H : SBasePawn.SA_FALL_ST_2H);
        }

        bTestBackFeetGrab = false;
        if (pawn is SPawnAttaque && fNormePadMove < MovementK.BackLedgeStickLimit && CheckBackFeetZone())
        {
            FVector start = pawn.Location;
            FVector end = start - new FVector(0f, 0f, MovementK.BackLedgeFloorProbe);
            if (MovementNatives.Trace(this, out _, out _, end, start, true, new FVector(pawn.CollisionRadius, pawn.CollisionRadius, pawn.CollisionHeight)) is not null)
            {
                eventChangeState(MovementStateNames.PlayerFalling);
            }
            else
            {
                bTestBackFeetGrab = true;
                pawn.Velocity = default;
                eventChangeState(MovementStateNames.PlayerFallToBackGrab);
            }

            return;
        }

        eventChangeState(MovementStateNames.PlayerFalling);
    }

    /// <summary>The common landing part: footprints, the landing time and the client's move flush. True.</summary>
    [Uc("SBase.SPlayerController.MyNotifyLanded", Sha1 = "c8ca688cf9d1b26766fe355895c85645bc801ddc")]
    [PortOf("uc:SBase.SPlayerController:MyNotifyLanded", Spec = "spec/movement/jump-fall-land.md#landing-s_playerjumpmynotifylanded-playerfalling-inherits-it")]
    public virtual bool MyNotifyLanded(FVector HitNormal, float fTimeLeft, Actor? LandedOn)
    {
        if (SBPawn is { } pawn)
        {
            IMovementHooks hooks = MovementService.HooksOf(this);
            hooks.FootPrint(pawn, false);
            hooks.FootPrint(pawn, true);
        }

        fTimeOfLastLanding = Level.TimeSeconds;
        if (Role < ENetRole.ROLE_Authority)
        {
            MyReplicateMoveWithoutMoving(fTimeLeft, Pawn?.Acceleration ?? default);
        }

        return true;
    }

    /// <summary>Below the world while falling: the out-of-world fall.</summary>
    [Uc("SBase.SPlayerController.FellOutOfWorld", Sha1 = "be2cc9c0bf306407f400fc1643d885ba27bc3c07")]
    [PortOf("uc:SBase.SPlayerController:FellOutOfWorld", Spec = "spec/movement/jump-fall-land.md#falling-out-of-the-world")]
    public virtual void FellOutOfWorld()
    {
        if (Pawn is { Physics: EPhysics.PHYS_Falling } && !IsInState(MovementStateNames.s_PlayerFalling_OutOfWorld))
        {
            eventChangeState(MovementStateNames.s_PlayerFalling_OutOfWorld);
        }
    }

    /// <summary>
    /// The resting state for the stance and hands, each with its action forced; a spy coming back from the pipe
    /// fire or Vandamme weapon states returns there without saving the state. Also a server-to-client call.
    /// </summary>
    [Uc("SBase.SPlayerController.GotoWaitingState", Sha1 = "26ff6ad677560f17e0ca4856ae3a69320b5ff3bf")]
    [PortOf("uc:SBase.SPlayerController:GotoWaitingState", Spec = "spec/movement/jump-fall-land.md#landing-states")]
    public virtual void GotoWaitingState()
    {
        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall("GotoWaitingState"));
            return;
        }

        if (SBPawn is not { } pawn)
        {
            return;
        }

        bool merc = pawn is SPAWNDEFENSE;
        if (!merc && (PreviousState == MovementStateNames.s_PipH_Fire2 || PreviousState == MovementStateNames.s_PipH_Fire4 || PreviousState == MovementStateNames.VandammeWeapon))
        {
            eventChangeState(PreviousState, default, true);
            return;
        }

        if (merc || pawn.WeaponLoaded > 0)
        {
            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_2H, true);
                eventChangeState(MovementStateNames.s_PlayerWalkingCr2H);
            }
            else
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2H, true);
                eventChangeState(MovementStateNames.s_PlayerWalkingSt2H);
            }
        }
        else if (pawn.bIsCrouched)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H, true);
            eventChangeState(MovementStateNames.s_PlayerWalkingCr0H);
        }
        else
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H, true);
            eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
        }
    }

    /// <summary>
    /// The fall damage rule: (h - Fell_MinHeightForDmg) / (Fell_MaxHeightForDmg - Fell_MinHeightForDmg) x
    /// default.Health above the minimum height, truncated, with h the drop from the apex.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "jump-fall-land.md fall damage formula; measured spy 100 (h - 600) / 900, merc 100 (h - 450) / 550")]
    public static int FallDamage(SBasePawn pawn, float h)
    {
        if (!(h > pawn.Fell_MinHeightForDmg))
        {
            return 0;
        }

        float dmg = (h - pawn.Fell_MinHeightForDmg) / (pawn.Fell_MaxHeightForDmg - pawn.Fell_MinHeightForDmg) *
            pawn.DefaultObject.Health;
        return MovementMath.Int(dmg);
    }
}
