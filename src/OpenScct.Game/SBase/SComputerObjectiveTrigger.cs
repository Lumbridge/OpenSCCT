using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// A Story terminal, written from spec/objectives/terminal-hack.md.
public partial class SComputerObjectiveTrigger
{
    /// <summary>SkinState values (screen material shown by the native tick).</summary>
    public const byte SKIN_Idle = 0;
    public const byte SKIN_Triggering = 1;
    public const byte SKIN_Locked = 2;
    public const byte SKIN_Hacked = 3;

    public static new readonly StateTable ClassStates = new(SObjectiveTrigger.ClassStates, s_TemporaryInactive.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>The terminal's "Use" entry.</summary>
    public SComputerInteraction? ComputerInteraction => InteractionObj as SComputerInteraction;

    // ---- setup (server) ---------------------------------------------------------------------------------------

    [Uc("SBase.SComputerObjectiveTrigger.PostBeginPlay", Sha1 = "027f4bf5e0182a82e76e1056130c98776f841ff6")]
    [PortOf("uc:SBase.SComputerObjectiveTrigger:PostBeginPlay", Spec = "spec/objectives/terminal-hack.md#rules")]
    protected override void PostBeginPlay()
    {
        SetCollision(false, false, false);
        Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        FVector at = Location + x * InteractionDistanceToCenter + y * UseRelativeLocation.Y + z * UseRelativeLocation.Z;
        InteractionObj = ObjectiveNatives.Spawn<SComputerInteraction>(this, this, at, Rotation);
        DeltaCaptureTime = 0f;
        SkinState = SKIN_Idle;
        base.PostBeginPlay();
    }

    [Uc("SBase.SComputerObjectiveTrigger.Destroyed", Sha1 = "d67b056f4d1c14c007b4b552aeacb3042b5bca00")]
    [PortOf("uc:SBase.SComputerObjectiveTrigger:Destroyed", Spec = "spec/objectives/terminal-hack.md#rules")]
    protected override void Destroyed()
    {
        InteractionObj?.Destroy();
        base.Destroyed();
    }

    // ---- reset and prepare (server, from the mission) ---------------------------------------------------------

    [Uc("SBase.SComputerObjectiveTrigger.Reset", Sha1 = "b542c4e188086807c06a06b35bf58b7e9d5f3613")]
    [PortOf("uc:SBase.SComputerObjectiveTrigger:Reset", Spec = "spec/objectives/terminal-hack.md#rules")]
    public override void Reset()
    {
        base.Reset();
        CaptureTime = DominationCaptureTime;
        bHidden = false;
        if (bTriggering)
        {
            eventTriggering(false);
        }

        SetCollision(true, true, BlocksPlayers);
        SkinState = SKIN_Idle;
    }

    [Uc("SBase.SComputerObjectiveTrigger.Prepare", Sha1 = "fe4eb1bafd97c20deade8b1d059156d149645c21")]
    [PortOf("uc:SBase.SComputerObjectiveTrigger:Prepare", Spec = "spec/objectives/terminal-hack.md#rules")]
    public override void Prepare()
    {
        bHidden = false;
        SetCollision(true, true, BlocksPlayers);
        SkinState = SKIN_Idle;
    }

    /// <summary>
    /// Whether a shown terminal blocks players: the script's Reset and Prepare call SetCollision(true, true, true).
    /// Players collide with the mesh's simple collision (two boxes reaching X 28.2), not its per-poly triangles (X
    /// 33.2), so a 35 uu spy fits at the 64 uu use spot (CollisionWorld.PairMeshSets). bBlockPlayers replicates, so
    /// clients follow the server.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SBase.SComputerObjectiveTrigger.Reset / Prepare SetCollision(true, true, true)")]
    public const bool BlocksPlayers = true;

    // ---- the hack ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Starts or stops a hack (rules 15 to 19). Stopping keeps the whole seconds hacked: CaptureTime loses the
    /// truncated elapsed time and never drops below 1.
    /// </summary>
    [Uc("SBase.SComputerObjectiveTrigger.Triggering", Sha1 = "03dc151b2a96d2f703ffd8228aa62d70a78c5466")]
    [PortOf("uc:SBase.SComputerObjectiveTrigger:Triggering", Spec = "spec/objectives/terminal-hack.md#rules")]
    protected internal override void Triggering(bool Status)
    {
        if (bTriggering && !Status)
        {
            int before = CaptureTime;
            CaptureTime = SvmMath.Max(CaptureTime - SvmMath.Trunc(Level.TimeSeconds - fHackStartTime), K.MinKeptCaptureTime);
            if (ControllerWhoTakesIt is SPlayerController { PlayerReplicationInfo.Stats: { } stats })
            {
                stats.Spy_ObjectiveTimeCaptured += before - CaptureTime;
            }
        }

        base.Triggering(Status);
        DeltaCaptureTime = 0f;
        if (Status)
        {
            fHackStartTime = Level.TimeSeconds;
            if (InteractionObj is not null)
            {
                InteractionObj.bUsable = false;
            }

            SetTimer(0f, false);
            SkinState = SKIN_Triggering;
            ObjectiveMessages.Broadcast(this, K.MsgNeutralizationInProgress, this);
        }
        else
        {
            if (Objective is not { ObjectiveTaken: true } && bActive && InteractionObj is not null)
            {
                InteractionObj.bUsable = true;
            }

            ControllerWhoTakesIt = null;
            if (bActive)
            {
                SetTimer(K.IdleSkinDelay, false);
            }
        }

        bTriggering = Status;
    }

    /// <summary>The screen goes back to idle 10 s after a hack stopped.</summary>
    [Uc("SBase.SComputerObjectiveTrigger.Timer", Sha1 = "bc263c8c523282a30c4d2ce49f5d4ff6275b5bfe")]
    [PortOf("uc:SBase.SComputerObjectiveTrigger:Timer", Spec = "spec/objectives/terminal-hack.md#rules")]
    protected override void Timer() => SkinState = SKIN_Idle;

    [Uc("SBase.SComputerObjectiveTrigger.TakeObjective", Sha1 = "201fbd041594b27c496a232ecba8c20737574832")]
    [PortOf("uc:SBase.SComputerObjectiveTrigger:TakeObjective", Spec = "spec/objectives/terminal-hack.md#rules")]
    protected internal override void TakeObjective()
    {
        base.TakeObjective();
        ObjectiveMessages.Broadcast(this, K.MsgNeutralized, this);
        SkinState = SKIN_Hacked;
        SetTimer(0f, false);
    }

    /// <summary>
    /// Takes the terminal out of play. First the taker, if any (alive or not, and not cleared), is sent back to
    /// walking with MirrorState('s_PlayerWalkingSt0H'): on a completed hack that runs inside the take, before
    /// s_Computer_Use.Timer sends him on to s_Computer_Leave, and a spy still hacking another terminal of a mission that
    /// completes is kicked out of it. In s_TemporaryInactive (an alarm lock) it then only clears the flags, sends
    /// Triggering(false) and turns the EMF glow off, keeping the state and the screen. Otherwise the trigger version,
    /// then the screen shows hacked.
    /// </summary>
    [UcNative("ASComputerObjectiveTrigger::execDesactivate", Exe = 0x10AE9C60)]
    [PortOf("nat:SBase.SComputerObjectiveTrigger.Desactivate", Spec = "spec/objectives/terminal-hack.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ABDEB0 (vtable +0x138): ControllerWhoTakesIt event MirrorState(name 0x444 = s_PlayerWalkingSt0H, hardcoded at 0x1092759E); in state 0x3C3 (s_TemporaryInactive) flags only; else ASObjectiveTrigger::Desactivate, then SkinState = 3")]
    public override void Desactivate()
    {
        // The native raises the MirrorState event on the controller; only an SPlayerController has that function.
        if (ControllerWhoTakesIt is SPlayerController taker)
        {
            ObjectiveNatives.MirrorState(taker, K.s_PlayerWalkingSt0H);
        }

        if (IsInState(K.s_TemporaryInactive))
        {
            DesactivateFlags();
            return;
        }

        DesactivateTrigger();
        SkinState = SKIN_Hacked;
    }

    // ---- alarm locks (not reachable in SteeE) -------------------------------------------------------------------

    [Uc("SBase.SComputerObjectiveTrigger.Lock", Sha1 = "39d74d10c75758a6cb463f3332c30a8488c7990e")]
    [PortOf("uc:SBase.SComputerObjectiveTrigger:Lock", Spec = "spec/objectives/terminal-hack.md#rules")]
    protected internal override void Lock()
    {
        if (Objective is { ObjectiveTaken: true })
        {
            return;
        }

        if (Objective is { RemainingTime: <= 0 })
        {
            Objective.RemainingTime = 1;
            CaptureTime += 1;
        }

        KickTaker();
        SetTimer(0f, false);
        GotoState(K.s_TemporaryInactive);
        if (bTriggering)
        {
            eventTriggering(false);
        }

        bLocked = true;
    }

    [Uc("SBase.SComputerObjectiveTrigger.UnLock", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SComputerObjectiveTrigger:UnLock", Spec = "spec/objectives/terminal-hack.md#rules")]
    protected internal override void UnLock()
    {
    }

    // While hacking, the living taker is sent back to walking and the trigger forgets him.
    internal void KickTaker()
    {
        if (bTriggering && ControllerWhoTakesIt is SPlayerController taker && ObjectiveNatives.IsAlive(taker))
        {
            ObjectiveNatives.MirrorState(taker, K.s_PlayerWalkingSt0H);
            ControllerWhoTakesIt = null;
        }
    }

    // ---- natives -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Per-tick native update (game 0x10ABDDD0, rule 31), on every machine: the screen material follows
    /// <see cref="SkinState"/>. Only the server advances the hack: <see cref="DeltaCaptureTime"/> grows by the tick
    /// while the terminal is triggering, active, unlocked and its objective not taken (CaptureTime only changes when a
    /// run stops). A client never touches the progress and shows the replicated value; it only clears its local
    /// bTriggering on a locked terminal.
    /// </summary>
    [UcNative("ASComputerObjectiveTrigger::Tick", Editor = 0x111CF690)]
    [PortOf("eng:ASComputerObjectiveTrigger::Tick", Spec = "spec/objectives/terminal-hack.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ABDDD0: authority DeltaCaptureTime += DeltaSeconds while bTriggering && bActive && !bLocked && !Objective.ObjectiveTaken; clients clear bTriggering when bLocked")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe)
        {
            return;
        }

        LocalSkinState = SkinState;
        if (Role == ENetRole.ROLE_Authority)
        {
            // The native reads Objective without a guard; a terminal without an objective keeps counting here.
            if (bTriggering && bActive && !bLocked && Objective is not { ObjectiveTaken: true })
            {
                DeltaCaptureTime += DeltaSeconds;
            }
        }
        else if (bLocked && bTriggering)
        {
            bTriggering = false;
        }
    }

    /// <summary>
    /// The terminal's capture percent (game 0x10ABDF80, rule 32): hacked seconds plus the current run, truncated
    /// toward zero. Not clamped here: it reaches 100 when the run reaches CaptureTime. The HUD clamps it to 0..99.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10ABDF80: trunc(((DominationCaptureTime - CaptureTime) + DeltaCaptureTime) * 100.0 / DominationCaptureTime)")]
    public int GetHackPercent()
    {
        // The native has no guard for DominationCaptureTime == 0 (a float division); 0 keeps the port defined.
        if (DominationCaptureTime == 0)
        {
            return 0;
        }

        float percent = (DominationCaptureTime - CaptureTime + DeltaCaptureTime) * 100f / DominationCaptureTime;
        return SvmMath.Trunc(percent);
    }

    /// <summary>
    /// The spy's use spot (s_Computer_GoTo rule 10): <c>Location + X * UseRelativeLocation.X + Y *
    /// UseRelativeLocation.Y</c> in the terminal's axes at the pawn's height, facing the terminal's yaw plus
    /// <c>UseRelativeRotation.Yaw</c> (the terminal's pitch and roll kept).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_Computer_GoTo.PlayerTick use spot")]
    public void GetUseSpot(Pawn pawn, out FVector Spot, out FVector Facing)
    {
        Rotation.GetAxes(out FVector x, out FVector y, out _);
        Spot = Location + x * UseRelativeLocation.X + y * UseRelativeLocation.Y;
        Spot.Z = pawn.Location.Z;
        Facing = new FRotator(Rotation.Pitch, Rotation.Yaw + UseRelativeRotation.Yaw, Rotation.Roll).Vector();
    }
}
