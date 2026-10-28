using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Objectives;

/// <summary>
/// Numbers of the objective and interaction rules (spec/objectives). Class defaults stay in the generated classes;
/// these are the values written inline in the original functions.
/// </summary>
public static class K
{
    // ---- interaction cylinders and the Use menu (interaction-system.md) ------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SInteractObject.PostBeginPlay: owner collision + 150")]
    public const float InteractionPadding = 150f;

    [Provenance(ProvenanceKind.Decoded, "SInteractionManager.PostBeginPlay: slots 0 to 4 are the fixed mate and Exit entries")]
    public const int FixedEntries = 5;

    [Provenance(ProvenanceKind.Decoded, "game 0x10ABF4C0: AddInteractionObj only inserts at slots 0 to 9")]
    public const int MenuInsertLastSlot = 9;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.DoInteraction: aStrafe^2 + aForward^2 > 4000^2 blocks the menu")]
    public const float MenuSpeedLimit = 4000f;

    [Provenance(ProvenanceKind.Decoded, "SPickupInteraction.PostBeginPlay: owner radius + 50")]
    public const float PickupRadiusPadding = 50f;

    [Provenance(ProvenanceKind.Decoded, "SPickupInteraction.PostBeginPlay: owner height + 10")]
    public const float PickupHeightPadding = 10f;

    [Provenance(ProvenanceKind.Decoded, "SFlagDropZoneInteraction.PostBeginPlay: 100 x 100 before PostNetBeginPlay restores the zone's size")]
    public const float DropZoneInteractionSize = 100f;

    [Provenance(ProvenanceKind.Estimated, "Native CanUse/Calc facing rule for bInteractInFront: both dot products above this (spec open question)")]
    public const float InFrontMinDot = 0f;

    // ---- terminals (terminal-hack.md) -----------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_Computer_GoTo.PlayerTick terminal search")]
    public const float TerminalGoToSearchRadius = 300f;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_Computer_Use terminal search")]
    public const float TerminalUseSearchRadius = 200f;

    [Provenance(ProvenanceKind.Decoded, "s_Computer_Use.BeginState / s_Hack_Use.BeginState fallback timer when no target is near")]
    public const float NoTargetFallbackTimer = 3.0f;

    [Provenance(ProvenanceKind.Decoded, "s_Computer_Use / s_Hack_Use / s_Maintain_Use PlayerMove: |aStrafe| or |aForward| over 1000 leaves")]
    public const float CancelInputThreshold = 1000f;

    [Provenance(ProvenanceKind.Decoded, "SComputerObjectiveTrigger.Triggering(false): SetTimer(10.0) back to the idle skin")]
    [Provenance(ProvenanceKind.Measured, "RE+ lab-movement L4: skin reset 9 to 11 s after release")]
    public const float IdleSkinDelay = 10.0f;

    [Provenance(ProvenanceKind.Decoded, "SComputerObjectiveTrigger.Triggering(false): CaptureTime never below 1")]
    public const int MinKeptCaptureTime = 1;

    // ---- switches (switch-hack.md) --------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_Hack_Use switch search")]
    public const float SwitchHackSearchRadius = 200f;

    [Provenance(ProvenanceKind.Decoded, "s_Hack_GoTo / s_Maintain_GoTo: use spot raised 27.5 when crouched")]
    public const float CrouchedUseSpotLift = 27.5f;

    /// <summary>Interaction sub-index the hack and hold states send with Interaction(): start of a hack or hold.</summary>
    [Provenance(ProvenanceKind.Decoded, "s_Hack_Enter / s_Maintain_Enter: Interaction(UsedInterraction, 2)")]
    public const int SubStart = 2;

    /// <summary>InteractionPost sub-index for a completed hack.</summary>
    [Provenance(ProvenanceKind.Decoded, "s_Hack_Leave label TriggerHack_Successfull: InteractionPost(.., 1)")]
    public const int SubSuccess = 1;

    /// <summary>InteractionPost sub-index for an aborted hack or hold.</summary>
    [Provenance(ProvenanceKind.Decoded, "s_Hack_Leave label TriggerHack_Failed, s_Hack_FastLeave: InteractionPost(.., 0)")]
    public const int SubFailed = 0;

    /// <summary>InteractionPost sub-index sent when a hold starts.</summary>
    [Provenance(ProvenanceKind.Decoded, "s_Maintain_Use.BeginState: InteractionPost(UsedInterraction, 3)")]
    public const int SubHoldStarted = 3;

    // ---- disk and drop zones (disk-flag.md) -----------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "s_FlagAndDropZone_GoTo: stand 67 uu in front of the disk or zone")]
    public const float FlagStandOff = 67f;

    [Provenance(ProvenanceKind.Decoded, "SFlagDropZone.Open: lid pitch + 5000")]
    public const int LidOpenPitch = 5000;

    [Provenance(ProvenanceKind.Decoded, "SFlagDropZone.PostBeginPlay: Cover RotationRate pitch 20000")]
    public const int LidRotationRate = 20000;

    [Provenance(ProvenanceKind.Decoded, "SFlagDropZone.Close: SetTimer(1.5)")]
    public const float LidCloseDelay = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "SFlagDropZone.PostBeginPlay: lid at Location + (0, 0, 10) - 10 X")]
    public const float LidOffsetUp = 10f;

    [Provenance(ProvenanceKind.Decoded, "SFlagDropZone.PostBeginPlay: lid at Location + (0, 0, 10) - 10 X")]
    public const float LidOffsetBack = 10f;

    [Provenance(ProvenanceKind.Decoded, "SFlag.s_TemporaryInactive.UnLock: SetTimer(1.0)")]
    public const float UnlockReenableDelay = 1.0f;

    [Provenance(ProvenanceKind.Decoded, "SFlag.TakeObjective: nearest zone search starts at 100000000")]
    public const float NearestZoneStart = 100000000f;

    public static readonly Name BoneThigh = new("B L Thigh");

    public static readonly Name BoneHand = new("B L Hand");

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.PickupFlag / s_Pickup.EndState: thigh offset (10, 0, -10)")]
    public static readonly FVector ThighOffset = new(10f, 0f, -10f);

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.PickupFlag: thigh rotation 0")]
    public static readonly FRotator ThighRotationTouch = new(0, 0, 0);

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_Pickup.EndState: thigh rotation (0, 0, 32768)")]
    public static readonly FRotator ThighRotationMenu = new(0, 0, 32768);

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimNotifyPickup / s_PutFlagInDropZone: hand offset (8, -5, 0)")]
    public static readonly FVector HandOffset = new(8f, -5f, 0f);

    [Provenance(ProvenanceKind.Decoded, "s_PutFlagInDropZone.BeginState: hand rotation (-4000, 0, 0)")]
    public static readonly FRotator HandRotationDrop = new(-4000, 0, 0);

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimNotifyPickup: hand rotation (-4000, 0, 16768)")]
    public static readonly FRotator HandRotationPickup = new(-4000, 0, 16768);

    // ---- missions (missions.md) -----------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SObjective.PreBeginPlay / PostBeginPlay: RemainingTime = 1000")]
    public const int ObjectiveRemainingTime = 1000;

    [Provenance(ProvenanceKind.Decoded, "Engine.LevelInfo const AP_A_TakeObjective")]
    public const int AP_A_TakeObjective = 15;

    [Provenance(ProvenanceKind.Decoded, "SObjective.SimulateBeingTook: 5.0 s with no trigger")]
    public const float SimulateBeingTookFallback = 5.0f;

    // ---- teams and message switches (messages.md) -----------------------------------------------------------

    /// <summary>Team index of the spies (SPAWNOFFENSE).</summary>
    public const int TeamSpies = 0;

    /// <summary>Team index of the mercenaries (SPAWNDEFENSE).</summary>
    public const int TeamMercs = 1;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 3: the objective's Description (spies)")]
    public const int MsgMissionSpy = 3;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 10: TriggerName @ \": Data stolen\"")]
    public const int MsgDataStolen = 10;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 11: TriggerName @ \": Data dropped\"")]
    public const int MsgDataDropped = 11;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 12: TriggerName @ \": Data secured\"")]
    public const int MsgDataSecured = 12;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 15: the objective's DescriptionDEF (mercs)")]
    public const int MsgMissionMerc = 15;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 19: the switch's sWarnHackingText")]
    public const int MsgWarnHacking = 19;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 22: TriggerName @ \": Data captured\"")]
    public const int MsgDataCaptured = 22;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 23: Neutralization process interrupted")]
    public const int MsgInterrupted = 23;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 36: TriggerName @ \": Neutralization in progress\"")]
    public const int MsgNeutralizationInProgress = 36;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 37: Objective.ObjectiveName @ \": Neutralized\"")]
    public const int MsgNeutralized = 37;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 39: Hacking in progress...")]
    public const int MsgHackingInProgress = 39;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 53: Go to the drop zone (secured case)")]
    public const int MsgGoToDropZone = 53;

    [Provenance(ProvenanceKind.Decoded, "SGameMessages switch 54: Retrieve the hard drive")]
    public const int MsgRetrieveHardDrive = 54;

    // ---- state names used across this area ------------------------------------------------------------------

    public static readonly Name s_Initial = new("s_Initial");
    public static readonly Name s_Inactive = new("s_Inactive");
    public static readonly Name s_TemporaryInactive = new("s_TemporaryInactive");
    public static readonly Name s_Hacking = new("s_Hacking");
    public static readonly Name s_Maintain_Use = new("s_Maintain_Use");
    public static readonly Name WaitingBeforeActivation = new("WaitingBeforeActivation");
    public static readonly Name WaitingBeforeDesactivation = new("WaitingBeforeDesactivation");
    public static readonly Name s_RestoreAfterAlarm = new("s_RestoreAfterAlarm");
    public static readonly Name s_PickedUp = new("s_PickedUp");
    public static readonly Name s_Flying = new("s_Flying");

    public static readonly Name s_MenuInteraction = new("s_MenuInteraction");
    public static readonly Name s_InteractWithObjectWaitingEvent = new("s_InteractWithObjectWaitingEvent");
    public static readonly Name s_InteractWithObjectWaitingEventCrToSt = new("s_InteractWithObjectWaitingEventCrToSt");
    public static readonly Name s_Computer_GoTo = new("s_Computer_GoTo");
    public static readonly Name s_Computer_Enter = new("s_Computer_Enter");
    public static readonly Name s_Computer_Use = new("s_Computer_Use");
    public static readonly Name s_Computer_Leave = new("s_Computer_Leave");
    public static readonly Name s_Computer_FastLeave = new("s_Computer_FastLeave");
    public static readonly Name s_Hack_GoTo = new("s_Hack_GoTo");
    public static readonly Name s_Hack_Enter = new("s_Hack_Enter");
    public static readonly Name s_Hack_Use = new("s_Hack_Use");
    public static readonly Name s_Hack_Leave = new("s_Hack_Leave");
    public static readonly Name s_Hack_FastLeave = new("s_Hack_FastLeave");
    public static readonly Name s_Maintain_GoTo = new("s_Maintain_GoTo");
    public static readonly Name s_Maintain_Enter = new("s_Maintain_Enter");
    public static readonly Name s_Maintain_Leave = new("s_Maintain_Leave");
    public static readonly Name s_FlagAndDropZone_GoTo = new("s_FlagAndDropZone_GoTo");
    public static readonly Name s_Pickup_GoTo = new("s_Pickup_GoTo");
    public static readonly Name s_Pickup = new("s_Pickup");
    public static readonly Name s_PutFlagInDropZone = new("s_PutFlagInDropZone");
    public static readonly Name s_PlayerWalkingSt0H = new("s_PlayerWalkingSt0H");
    public static readonly Name s_PlayerWalkingCr0H = new("s_PlayerWalkingCr0H");
    public static readonly Name GameEnded = new("GameEnded");
    public static readonly Name TriggerHack_Successfull = new("TriggerHack_Successfull");
    public static readonly Name TriggerHack_Failed = new("TriggerHack_Failed");
}
