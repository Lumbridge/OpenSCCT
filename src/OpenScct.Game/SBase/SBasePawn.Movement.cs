using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn movement: the crouch natives and events, the jump native, the GE search zones, the traversal data
// sent to the server, the landing sound and the pawn-side AnimEnd bookkeeping. Written from spec/movement/walking.md,
// jump-fall-land.md, state-and-network.md, pawn.md and native-contracts.md.
public partial class SBasePawn
{
    private SBasePawn? _defaults;

    /// <summary>The pawn's animation channels (the skeletal mesh instance).</summary>
    public PawnAnimation PawnAnim { get; } = new();

    /// <summary>This class's default object (UnrealScript <c>default.X</c>).</summary>
    protected SBasePawn Defaults => _defaults ??= GetUcClass().Create() as SBasePawn ?? this;

    /// <summary>The default object, for the movement natives.</summary>
    internal SBasePawn DefaultObject => Defaults;

    /// <summary>Scratch list for the GE zone searches (no allocation per tick).</summary>
    internal readonly System.Collections.Generic.List<OpenScct.Physics.Traversal.GEHit> GEScratch = new(16);

    // ---- per tick -------------------------------------------------------------------------------------------

    /// <summary>
    /// The pawn's tick, after its controller's: with the movement layer installed the animation channels advance
    /// first (ASBasePawn::Tick updates the mesh before TickAuthoritative), then the script tick and the physics. The
    /// post-tick then updates the movement animation (the climb steps) and OldPhysics keeps the mode the tick ended
    /// in (AnimateWalking reads it on the next tick).
    /// </summary>
    [UcNative("ASBasePawn::Tick", Editor = 0x11205fb0)]
    [Provenance(ProvenanceKind.Decoded, "ASBasePawn::Tick 0x11205fb0 ends with the post-tick 0x11205ef0: UpdateMovementAnimation (vtable 0x160) with bPhysicsAnimUpdate, then OldPhysics = Physics")]
    [Provenance(ProvenanceKind.Decoded, "post-tick 0x11205ef0 then runs the script event UpdateEyeHeight when bUpdateEyeheight (spec/native/ANSWERS-anim.md A9)")]
    protected override void NativeTick(float DeltaSeconds)
    {
        // The tick starts with the rain check (SBasePawn.Rain.cs).
        TickRain(DeltaSeconds);
        AdvanceAnimation();
        base.NativeTick(DeltaSeconds);
        if (!bDeleteMe)
        {
            UpdateMovementAnimation(DeltaSeconds);
            OldPhysics = Physics;

            // The post-tick ends with the script event UpdateEyeHeight while bUpdateEyeheight (SBasePawn.EyeHeight.cs).
            if (bUpdateEyeheight)
            {
                UpdateEyeHeight(DeltaSeconds);
            }
        }
    }

    /// <summary>
    /// Advances the animation channels once per level tick (by that tick's time) and raises AnimEnd on this pawn,
    /// then on its controller, for every one-shot that ends.
    /// </summary>
    public void AdvanceAnimation()
    {
        if (MovementService.Of(this) is null || XLevel is null || PawnAnim.LastAdvanceTick == XLevel.TickCount)
        {
            return;
        }

        PawnAnim.LastAdvanceTick = XLevel.TickCount;
        PawnAnim.Owner ??= this;
        PawnAnim.Advance(XLevel.DeltaTime, _raiseAnimEnd ??= RaiseAnimEnd, _inRootMotion ??= InRootMotion);
    }

    private System.Action<int>? _raiseAnimEnd;
    private System.Func<bool>? _inRootMotion;

    private bool InRootMotion() => Physics == EPhysics.PHYS_RootMotion;

    /// <summary>
    /// A one-shot's end: on the base channel of a root-motion move the native first places the pawn at the end of
    /// the move (EndRootMotion), then AnimEnd goes to the pawn and its controller.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "anim.md root motion rule 2: channel 0 at its last frame calls EndRootMotion")]
    [Provenance(ProvenanceKind.Estimated, "EndRootMotion runs before the script AnimEnd of the same clip end")]
    private void RaiseAnimEnd(int channel)
    {
        if (channel == 0 && Physics == EPhysics.PHYS_RootMotion && _rootMotion is { RootLockType: not 0 } &&
            MovementNatives.Driver(this) is { } driver)
        {
            driver.EndRootMotion(this);
            if (bDeleteMe)
            {
                return;
            }
        }

        eventAnimEnd(channel);
        if (Controller is { bDeleteMe: false } c)
        {
            c.eventAnimEnd(channel);
        }
    }

    // ---- crouch ---------------------------------------------------------------------------------------------

    /// <summary>True when a standing cylinder fits where the pawn would stand up (the driver's native).</summary>
    [UcNative("ASBasePawn::execCanUnCrouch", Exe = 0x10acc930, Editor = 0x11206900)]
    [PortOf("nat:SBase.SBasePawn.CanUnCrouch", Spec = "spec/movement/native-contracts.md#per-native-contracts")]
    public bool CanUnCrouch()
    {
        if (!bIsCrouched)
        {
            return true;
        }

        if (MovementNatives.Driver(this) is { } driver)
        {
            return driver.CanUnCrouch(this);
        }

        return true;
    }

    /// <summary>
    /// The native crouch (SA_WANTS_TO_CROUCH): the crouched cylinder (CrouchRadius, CrouchHeight), the centre moved
    /// down by ZCDecal[ZoneConfig * 4 + 1] so the feet stay put, PrePivot raised by the height change so the mesh
    /// stays on the floor, zone configuration 1, then StartCrouch with the height change. A simulated proxy only
    /// sets bIsCrouched (its cylinder follows the owner's zone configuration).
    /// </summary>
    [UcNative("ASBasePawn::Crouch", Editor = 0x11206990)]
    [Provenance(ProvenanceKind.Estimated, "walking.md: SA_WANTS_TO_CROUCH sets the crouched cylinder and keeps the feet; measured Z shift 37.5 spy, 19.5 merc")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md rule 5.6: PrePivot = default PrePivot plus the change in Z; a simulated proxy only sets bIsCrouched")]
    public bool Crouch()
    {
        if (bIsCrouched)
        {
            return true;
        }

        if (Role == ENetRole.ROLE_SimulatedProxy)
        {
            bIsCrouched = true;
            return true;
        }

        float heightAdjust = CollisionHeight - CrouchHeight;
        float decal = ZCDecal[ZoneConfig * 4 + 1];
        MovementNatives.SetCollisionSize(this, CrouchRadius, CrouchHeight);
        MovementNatives.FarMoveActor(this, Location - new FVector(0f, 0f, decal));
        PrePivot = Defaults.PrePivot + new FVector(0f, 0f, heightAdjust);
        bIsCrouched = true;
        ZoneConfig = 1;
        ZoneConfigForClient = 1;
        eventStartCrouch(heightAdjust);
        return true;
    }

    /// <summary>
    /// The native stand-up (SA_WANTS_TO_UNCROUCH, and the walking drop attack): when the standing cylinder fits, the
    /// default cylinder with the centre raised back by ZCDecal, zone configuration 0, then EndCrouch with the height
    /// change. True when the pawn stands afterwards.
    /// </summary>
    [UcNative("ASBasePawn::UnCrouch", Editor = 0x111fb750)]
    [Provenance(ProvenanceKind.Estimated, "walking.md: SA_WANTS_TO_UNCROUCH restores the cylinder when CanUnCrouch")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md rule 5.6: default PrePivot on standing up; a simulated proxy only clears bIsCrouched")]
    public bool UnCrouch(bool bClientSimulation)
    {
        if (!bIsCrouched)
        {
            return true;
        }

        if (Role == ENetRole.ROLE_SimulatedProxy)
        {
            bIsCrouched = false;
            return true;
        }

        if (!CanUnCrouch())
        {
            return false;
        }

        SBasePawn d = Defaults;
        float decal = ZCDecal[ZoneConfig * 4];
        float heightAdjust = d.CollisionHeight - CollisionHeight;
        PrePivot = d.PrePivot;
        MovementNatives.SetCollisionSize(this, d.CollisionRadius, d.CollisionHeight);
        MovementNatives.FarMoveActor(this, Location - new FVector(0f, 0f, decal));
        bIsCrouched = false;
        ZoneConfig = 0;
        ZoneConfigForClient = 0;
        eventEndCrouch(heightAdjust);
        return true;
    }

    /// <summary>
    /// The native UnCrouch() of the down-from-the-top moves (actions 149, 150, 163): the default cylinder and the
    /// standing height with no room test (moved up through the world) and no EndCrouch event.
    /// </summary>
    [UcNative("ASBasePawn::UnCrouch", Editor = 0x111fb750)]
    [PortOf("eng:ASBasePawn::UnCrouch", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md helper 6: UnCrouch() without the trace, world collision off, no event")]
    [Provenance(ProvenanceKind.Estimated, "execute-action.md helper 6 says UnCrouch() does the same as UnCrouch(false): a simulated proxy only clears bIsCrouched")]
    public void UnCrouchNoCheck()
    {
        if (!bIsCrouched)
        {
            return;
        }

        if (Role == ENetRole.ROLE_SimulatedProxy)
        {
            bIsCrouched = false;
            return;
        }

        SBasePawn d = Defaults;
        float decal = ZCDecal[ZoneConfig * 4];
        PrePivot = d.PrePivot;
        MovementNatives.SetCollisionSize(this, d.CollisionRadius, d.CollisionHeight);
        MovementNatives.FarMoveActor(this, Location - new FVector(0f, 0f, decal));
        bIsCrouched = false;
        ZoneConfig = 0;
        ZoneConfigForClient = 0;
    }

    /// <summary>Room to leave the zip speed pose (the driver's UnZipSpeed test).</summary>
    [UcNative("ASBasePawn::execCanUnZipSpeed", Exe = 0x10acce10, Editor = 0x11206b70)]
    [PortOf("nat:SBase.SBasePawn.CanUnZipSpeed", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
    public bool CanUnZipSpeed() => MovementNatives.Driver(this) is not { } driver || driver.CanUnZipSpeed(this);

    public void eventStartCrouch(float HeightAdjust) => StartCrouch(HeightAdjust);

    public void eventEndCrouch(float HeightAdjust) => EndCrouch(HeightAdjust);

    /// <summary>Engine.Pawn: the eye stays where it was this frame and the crouched eye height is the new base.</summary>
    [Uc("Engine.Pawn.StartCrouch", Sha1 = "c13a34766404943059443d57d452636115abe979")]
    [PortOf("uc:Engine.Pawn:StartCrouch", Spec = "spec/movement/walking.md#crouch-requests-processcrouch")]
    public virtual void StartCrouch(float HeightAdjust)
    {
        EyeHeight += HeightAdjust;
        OldZ -= HeightAdjust;
        BaseEyeHeight = SvmMath.Min(0.8f * CrouchHeight, CrouchHeight - 10f);
    }

    /// <summary>Engine.Pawn: the reverse of StartCrouch, back to the default eye height.</summary>
    [Uc("Engine.Pawn.EndCrouch", Sha1 = "cc3c7f6253381e8a13340aaa1a4fccf07dbed919")]
    [PortOf("uc:Engine.Pawn:EndCrouch", Spec = "spec/movement/walking.md#crouch-requests-processcrouch")]
    public virtual void EndCrouch(float HeightAdjust)
    {
        EyeHeight -= HeightAdjust;
        OldZ += HeightAdjust;
        BaseEyeHeight = Defaults.BaseEyeHeight;
    }

    /// <summary>The fence face test: 0 nothing, 1 a climbable fence face, 2 a flat face that is not climbable.</summary>
    [UcNative("ASBasePawn::execCheckFence", Exe = 0x10acf300, Editor = 0x112085a0)]
    [PortOf("nat:SBase.SBasePawn.CheckFence", Spec = "spec/native/physics-ge-queries.md#10-checkfenceout-normal-out-wall-pos---int")]
    public int CheckFence(out FVector Normal, out Actor? Wall, FVector pos)
    {
        if (MovementNatives.Driver(this) is { } driver)
        {
            return driver.CheckFence(this, out Normal, out Wall, pos);
        }

        Normal = default;
        Wall = null;
        return 0;
    }

    // ---- jump -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The jump impulse (the driver's native, by physics mode: a walking jump, the push-off from a fence, pipe or
    /// ladder, the second jump, nothing on a ledge). Without physics only the walking jump applies.
    /// </summary>
    [UcNative("ASBasePawn::MyDoJump", Exe = 0x10ae00b0, Editor = 0x111f9cf0)]
    [PortOf("nat:SBase.SBasePawn.MyDoJump", Spec = "spec/native/ANSWERS-fidelity.md#f2-mydojump")]
    public void MyDoJump()
    {
        if (MovementNatives.Driver(this) is { } driver)
        {
            driver.MyDoJump(this);
            return;
        }

        if (Physics != EPhysics.PHYS_Walking)
        {
            return;
        }

        Velocity.Z = JumpZ;
        nJumpNumber = 1;
        Physics = EPhysics.PHYS_Falling;
    }

    // ---- actions --------------------------------------------------------------------------------------------

    /// <summary>
    /// Plays a movement action on the controlling side: executes it when it differs from the current one (or when
    /// forced) and queues it for the server and the other players (at most 6 per 0.06 s batch).
    /// </summary>
    [Uc("SBase.SBasePawn.ChangeEnumAction", Sha1 = "126ce464c4a14b71c9084e9058e5c9f03ca1d056")]
    [PortOf("uc:SBase.SBasePawn:ChangeEnumAction", Spec = "spec/movement/state-and-network.md#actions-changeenumaction-and-executeaction")]
    public virtual void ChangeEnumAction(int Action, bool bForcePlayAnim = false)
    {
        if (Controller is null || Controller.RemoteRole == ENetRole.ROLE_AutonomousProxy)
        {
            return;
        }

        if (Action == sAction && !bForcePlayAnim)
        {
            return;
        }

        sAction = Action;
        ExecuteAction(Action);
        float now = Level.TimeSeconds;
        if (now - fTimeOfLastActionChange > ActionBatchWindow && iNbActionsSent >= iNbActionInTable)
        {
            iNbActionInTable = 0;
            fTimeOfLastActionChange = now;
        }

        if (iNbActionInTable < C_MAXACTIONS)
        {
            AllActions[iNbActionInTable] = Action;
            iNbAction++;
            iNbActionInTable++;
        }
    }

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ChangeEnumAction batch window 0.06 s")]
    private const float ActionBatchWindow = 0.06f;

    /// <summary>The native action player (clips, physics mode, crouch cylinder), see <see cref="PawnActions"/>.</summary>
    [UcNative("ASBasePawn::execExecuteAction", Exe = 0x10ad6800, Editor = 0x1121eb10)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/movement/native-contracts.md#per-native-contracts")]
    public void ExecuteAction(int Action)
    {
        OpenScct.Game.Animation.ActionAnimation.Entry entry = OpenScct.Game.Animation.ActionAnimation.Begin(this, Action);
        PawnActions.Execute(this, Action);
        OpenScct.Game.Animation.ActionAnimation.End(this, Action, entry);
    }

    /// <summary>
    /// Once per tick on the controlling side: the queued actions go to the server unless a server move already
    /// carried them. On the machine that is the server (standalone, listen host) there is nothing to send.
    /// </summary>
    [Uc("SBase.SBasePawn.SendActions", Sha1 = "f7b2c158586b206383c577fd4994baa4ea29122a")]
    [PortOf("uc:SBase.SBasePawn:SendActions", Spec = "spec/movement/state-and-network.md#actions-changeenumaction-and-executeaction")]
    public virtual void SendActions()
    {
        if (iNbAction > 0 && !bActionsHasBeenSentByServerMove && Role < ENetRole.ROLE_Authority &&
            Controller is SPlayerController pc)
        {
            MovementNet.Of(this).ReplicateActionToServer(pc, AllActions, iNbActionInTable, bActionsSendWithVelocity);
        }

        if (iNbAction > 0)
        {
            iNbActionsSent = iNbActionInTable;
            iNbAction = 0;
        }

        bActionsSendWithVelocity = false;
        bActionsHasBeenSentByServerMove = false;
        bActionsSendByServerMove = false;
    }

    // ---- GE search zones ------------------------------------------------------------------------------------

    /// <summary>The normal arms sphere, and the feet sphere for the stance.</summary>
    [Uc("SBase.SBasePawn.ResetZones", Sha1 = "70c89f1afc6b114f9a3e489a2fb70a09a9e77dcb")]
    [PortOf("uc:SBase.SBasePawn:ResetZones", Spec = "spec/movement/walking.md#zones-resetzones-setnormalzones")]
    public virtual void ResetZones()
    {
        m_CurrentArmsZone = m_NormalArmsZone;
        m_CurrentArmsRadius = m_NormalArmsRadius;
        if (bIsCrouched)
        {
            m_CurrentFeetZone = m_CrouchedFeetZone;
            m_CurrentFeetRadius = m_CrouchedFeetRadius;
        }
        else
        {
            m_CurrentFeetZone = m_NormalFeetZone;
            m_CurrentFeetRadius = m_NormalFeetRadius;
        }
    }

    /// <summary>The normal arms sphere and the standing feet sphere.</summary>
    [Uc("SBase.SBasePawn.SetNormalZones", Sha1 = "f117adcf7dc596faaaa0840662444734b182857b")]
    [PortOf("uc:SBase.SBasePawn:SetNormalZones", Spec = "spec/movement/walking.md#zones-resetzones-setnormalzones")]
    public virtual void SetNormalZones()
    {
        m_CurrentArmsZone = m_NormalArmsZone;
        m_CurrentArmsRadius = m_NormalArmsRadius;
        m_CurrentFeetZone = m_NormalFeetZone;
        m_CurrentFeetRadius = m_NormalFeetRadius;
    }

    [Uc("SBase.SBasePawn.SetLedgeGrabZones", Sha1 = "01c04b8a78f0095e5526e401f8b6a1ab88a894c0")]
    [PortOf("uc:SBase.SBasePawn:SetLedgeGrabZones", Spec = "spec/movement/ledge.md#grabledge-simulated")]
    public virtual void SetLedgeGrabZones()
    {
        m_CurrentArmsZone = m_LedgeGrabArmsZone;
        m_CurrentArmsRadius = m_LedgeGrabArmsRadius;
    }

    [Uc("SBase.SBasePawn.SetPipeZones", Sha1 = "f2404e82e1b253a92a0ec94e766725fb9c580ccc")]
    [PortOf("uc:SBase.SBasePawn:SetPipeZones", Spec = "spec/movement/walking.md#zones-resetzones-setnormalzones")]
    public virtual void SetPipeZones()
    {
        m_CurrentArmsZone = m_PipeArmsZone;
        m_CurrentArmsRadius = m_PipeArmsRadius;
    }

    [Uc("SBase.SBasePawn.SetZipLineZones", Sha1 = "9ea0c2384deeda8b47205e4867ac1e62461f9284")]
    [PortOf("uc:SBase.SBasePawn:SetZipLineZones", Spec = "spec/movement/walking.md#zones-resetzones-setnormalzones")]
    public virtual void SetZipLineZones()
    {
        m_CurrentArmsZone = m_ZipLineArmsZone;
        m_CurrentArmsRadius = m_ZipLineArmsRadius;
    }

    [Uc("SBase.SBasePawn.SetFenceZones", Sha1 = "1b90da12a36f0dc5eaafdac2dbe70b6569cfdddd")]
    [PortOf("uc:SBase.SBasePawn:SetFenceZones", Spec = "spec/movement/walking.md#zones-resetzones-setnormalzones")]
    public virtual void SetFenceZones()
    {
        m_CurrentArmsZone = m_FenceArmsZone;
        m_CurrentArmsRadius = m_FenceArmsRadius;
    }

    // ---- traversal data to the server -----------------------------------------------------------------------

    /// <summary>The GE points the client found go to the server (ZipP and NewZipP there).</summary>
    [Uc("SBase.SBasePawn.SendZipPToServer", Sha1 = "8228275d0d867d82890b691793772e37c7a7952e")]
    [PortOf("uc:SBase.SBasePawn:SendZipPToServer", Spec = "spec/movement/state-and-network.md#traversal-data-to-the-server")]
    public virtual void SendZipPToServer()
    {
        // The held GE is also the candidate one, so the pipe swap test (physOnPipe) sees no new pipe.
        NewZipP1 = ZipP1;
        NewZipP2 = ZipP2;
        if (Role < ENetRole.ROLE_Authority && Controller is SPlayerController pc)
        {
            MovementNet.Of(this).ServerGetZipP(pc, ZipP1, ZipP2);
        }
    }

    [Uc("SBase.SBasePawn.SendNewZipPToServer", Sha1 = "30e627eff066322560da2c326e3e211f02e32783")]
    [PortOf("uc:SBase.SBasePawn:SendNewZipPToServer", Spec = "spec/movement/state-and-network.md#traversal-data-to-the-server")]
    public virtual void SendNewZipPToServer()
    {
        if (Role < ENetRole.ROLE_Authority && Controller is SPlayerController pc)
        {
            MovementNet.Of(this).ServerGetNewZipP(pc, NewZipP1, NewZipP2);
        }
    }

    /// <summary>The GE points and the GE normal (sent x 1000, rounded per component).</summary>
    [Uc("SBase.SBasePawn.SendZipPAndNormalToServer", Sha1 = "f9af431a4ffcdcfdcfeaa83275f013aa89b2f98e")]
    [PortOf("uc:SBase.SBasePawn:SendZipPAndNormalToServer", Spec = "spec/movement/state-and-network.md#traversal-data-to-the-server")]
    public virtual void SendZipPAndNormalToServer()
    {
        NewZipP1 = ZipP1;
        NewZipP2 = ZipP2;
        if (Role < ENetRole.ROLE_Authority && Controller is SPlayerController pc)
        {
            MovementNet.Of(this).ServerGetZipPAndNormal(pc, ZipP1, ZipP2, MovementNet.Scaled(m_geoNormal, 1000f));
        }
    }

    /// <summary>The repositioning target facing, with the target spot and the start time (sent x 1000).</summary>
    [Uc("SBase.SBasePawn.SetInfoForVandamme", Sha1 = "05ed3f26ac41431c0bb6235b478866610907c358")]
    [PortOf("uc:SBase.SBasePawn:SetInfoForVandamme", Spec = "spec/movement/state-and-network.md#traversal-data-to-the-server")]
    public virtual void SetInfoForVandamme(FVector wallDir)
    {
        vWallJumpDir = wallDir;
        fLastRootMotionTimeCalled = Level.TimeSeconds;
        if (Role < ENetRole.ROLE_Authority && Controller is SPlayerController pc)
        {
            MovementNet.Of(this).ServerSetInfoForVandamme(pc, vVandammePos, MovementNet.Scaled(wallDir, 1000f));
        }
    }

    [Uc("SBase.SBasePawn.SetWallJumpDir", Sha1 = "ec4e53d3911beb055425084c4156fb5f6cf709a5")]
    [PortOf("uc:SBase.SBasePawn:SetWallJumpDir", Spec = "spec/movement/state-and-network.md#traversal-data-to-the-server")]
    public virtual void SetWallJumpDir(FVector wallDir)
    {
        vWallJumpDir = wallDir;
        if (Role < ENetRole.ROLE_Authority && Controller is SPlayerController pc)
        {
            MovementNet.Of(this).ServerSetWallJumpDir(pc, MovementNet.Scaled(wallDir, 1000f));
        }
    }

    // ---- landing, damage and base hooks ---------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.PlayLandingSound: Sound_Persos.FootSteps.SW_Landing, volume 1.0, radius 300")]
    private static readonly AssetRef LandingSound = new("Sound_Persos.FootSteps.SW_Landing");

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.PlayLandingSound radius 300")]
    private const float LandingSoundRadius = 300f;

    /// <summary>
    /// Plays the landing sound here; the authority also tells the other players' machines, a client asks the
    /// server to play it for everyone.
    /// </summary>
    [Uc("SBase.SBasePawn.PlayLandingSound", Sha1 = "4d7641f6f52177053ce16240de4a7ad406591540")]
    [PortOf("uc:SBase.SBasePawn:PlayLandingSound", Spec = "spec/movement/jump-fall-land.md#network")]
    public virtual void PlayLandingSound()
    {
        PlaySound(LandingSound, ESoundSlot.SLOT_None, 1f, false, LandingSoundRadius);
        if (Role == ENetRole.ROLE_Authority)
        {
            ClientPlayLandingSound();
        }
        else if (Controller is SPlayerController pc)
        {
            MovementNet.Of(this).ServerPlayLandingSound(pc);
        }
    }

    /// <summary>Server to every client: plays only where the pawn has no controller (the other players).</summary>
    [Uc("SBase.SBasePawn.ClientPlayLandingSound", Sha1 = "05119816cd8737b8101ef66b26371e1003d755f8")]
    [PortOf("uc:SBase.SBasePawn:ClientPlayLandingSound", Spec = "spec/movement/jump-fall-land.md#network")]
    public virtual void ClientPlayLandingSound()
    {
        if (Level.NetMode == ENetMode.NM_Client && Controller is null)
        {
            PlaySound(LandingSound, ESoundSlot.SLOT_None, 1f, false, LandingSoundRadius);
        }
    }

    /// <summary>Client to server: plays on the server and multicasts.</summary>
    [Uc("SBase.SBasePawn.ServerPlayLandingSound", Sha1 = "2cee024ec67c557473c197c242c4462cc7282c9a")]
    [PortOf("uc:SBase.SBasePawn:ServerPlayLandingSound", Spec = "spec/movement/jump-fall-land.md#network")]
    public virtual void ServerPlayLandingSound()
    {
        PlaySound(LandingSound, ESoundSlot.SLOT_None, 1f, false, LandingSoundRadius);
        ClientPlayLandingSound();
    }

    /// <summary>Empty: fall damage is the controller's rule (jump-fall-land.md).</summary>
    [Uc("SBase.SBasePawn.TakeFallingDamage", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SBasePawn:TakeFallingDamage", Spec = "spec/movement/pawn.md#crushed-by-movers")]
    public virtual void TakeFallingDamage()
    {
    }

    /// <summary>Empty: the stock "jump off a pawn" push does not happen, a pawn landing on another stays there.</summary>
    [Uc("SBase.SBasePawn.BaseChange", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SBasePawn:BaseChange", Spec = "spec/movement/pawn.md#crushed-by-movers")]
    protected override void BaseChange()
    {
    }

    /// <summary>The sequence playing on an animation channel.</summary>
    [Uc("SBase.SBasePawn.GetAnimName", Sha1 = "e225bcedd19124bbd4a3de1ef24f6b318b28f1bf")]
    [PortOf("uc:SBase.SBasePawn:GetAnimName", Spec = "spec/movement/pawn.md#animend-bookkeeping-sbasepawnanimend-before-the-controllers-animend")]
    public virtual Name GetAnimName(int Channel) =>
        Channel >= 0 && Channel < PawnAnimation.ChannelCount ? PawnAnim[Channel].Sequence : Name.None;

    /// <summary>
    /// The pawn's own AnimEnd bookkeeping before the controller's (OpenScct.Game.Animation.PawnAnimEnd): the overlay
    /// channels blend out, the draw channel ends the weapon transit, the damage, goggle and weapon channels reset the
    /// current action so the same action can play again, the run channel ends the no-run flag, and channel 0 chains
    /// the clip that follows a one-shot.
    /// </summary>
    [Uc("SBase.SBasePawn.AnimEnd", Sha1 = "fade18fa570d1a22ba27624b816f8313f5476d66")]
    [PortOf("uc:SBase.SBasePawn:AnimEnd", Spec = "spec/movement/pawn.md#animend-bookkeeping-sbasepawnanimend-before-the-controllers-animend")]
    protected override void AnimEnd(int Channel) => OpenScct.Game.Animation.PawnAnimEnd.Channel(this, Channel);
}
