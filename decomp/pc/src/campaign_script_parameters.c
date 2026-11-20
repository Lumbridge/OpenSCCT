#include "campaign_script_events.h"

typedef int ScriptBoolean;
typedef struct Script_Actor *RefActor;
typedef struct Script_Controller *RefController;
typedef struct Script_EGameplayObject *RefEGameplayObject;
typedef struct Script_EGoal *RefEGoal;
typedef struct Script_EInteractObject *RefEInteractObject;
typedef struct Script_EInventory *RefEInventory;
typedef struct Script_EInventoryItem *RefEInventoryItem;
typedef struct Script_EPawn *RefEPawn;
typedef struct Script_EScopeAttachment *RefEScopeAttachment;
typedef struct Script_EVolume *RefEVolume;
typedef struct Script_Interaction *RefInteraction;
typedef struct Script_Pawn *RefPawn;
typedef struct Script_PlayerController *RefPlayerController;
typedef struct Script_ZoneInfo *RefZoneInfo;
typedef struct { RefActor value; } Argument_RefActor;
typedef struct { RefController value; } Argument_RefController;
typedef struct { RefEGameplayObject value; } Argument_RefEGameplayObject;
typedef struct { RefEInteractObject value; } Argument_RefEInteractObject;
typedef struct { RefEInventory value; } Argument_RefEInventory;
typedef struct { RefEInventoryItem value; } Argument_RefEInventoryItem;
typedef struct { RefEPawn value; } Argument_RefEPawn;
typedef struct { RefEScopeAttachment value; } Argument_RefEScopeAttachment;
typedef struct { RefEVolume value; } Argument_RefEVolume;
typedef struct { RefInteraction value; } Argument_RefInteraction;
typedef struct { RefPawn value; } Argument_RefPawn;
typedef struct { RefPlayerController value; } Argument_RefPlayerController;
typedef struct { RefZoneInfo value; } Argument_RefZoneInfo;
typedef struct { ScriptBoolean value; } Argument_ScriptBoolean;
typedef struct { ScriptEventName value; } Argument_ScriptEventName;
typedef struct { float value; } Argument_float;
typedef struct { int value; } Argument_int;

/* Every parameter block follows the field order in its script declaration. */

#define SCRIPT_QUERY_0(Method, Name, ResultType) \
    extern ScriptEventName name_##Name; \
    ResultType __fastcall Method(struct ScriptObject *object) \
    { \
        struct { ResultType result; } frame; \
        ParameterReference address; \
        frame.result = 0; \
        address.address = &frame; \
        object->methods->process_event.parameters( \
            object, find_script_function(object, name_##Name, 0.0f), address, 0.0f); \
        return frame.result; \
    }

#define SCRIPT_ACTION_1(Method, Name, T0, P0) \
    extern ScriptEventName name_##Name; \
    void __fastcall Method(struct ScriptObject *object, Argument_##T0 P0) \
    { \
        struct { T0 P0; } frame; \
        ParameterReference address; \
        frame.P0 = P0.value; \
        address.address = &frame; \
        object->methods->process_event.parameters( \
            object, find_script_function(object, name_##Name, 0.0f), address, 0.0f); \
    }

#define SCRIPT_QUERY_1(Method, Name, ResultType, T0, P0) \
    extern ScriptEventName name_##Name; \
    ResultType __fastcall Method(struct ScriptObject *object, Argument_##T0 P0) \
    { \
        struct { T0 P0; ResultType result; } frame; \
        ParameterReference address; \
        frame.result = 0; \
        frame.P0 = P0.value; \
        address.address = &frame; \
        object->methods->process_event.parameters( \
            object, find_script_function(object, name_##Name, 0.0f), address, 0.0f); \
        return frame.result; \
    }

#define SCRIPT_ACTION_2(Method, Name, T0, P0, T1, P1) \
    extern ScriptEventName name_##Name; \
    void __fastcall Method(struct ScriptObject *object, Argument_##T0 P0, Argument_##T1 P1) \
    { \
        struct { T0 P0; T1 P1; } frame; \
        ParameterReference address; \
        frame.P0 = P0.value; \
        frame.P1 = P1.value; \
        address.address = &frame; \
        object->methods->process_event.parameters( \
            object, find_script_function(object, name_##Name, 0.0f), address, 0.0f); \
    }

#define SCRIPT_ACTION_3(Method, Name, T0, P0, T1, P1, T2, P2) \
    extern ScriptEventName name_##Name; \
    void __fastcall Method(struct ScriptObject *object, Argument_##T0 P0, Argument_##T1 P1, Argument_##T2 P2) \
    { \
        struct { T0 P0; T1 P1; T2 P2; } frame; \
        ParameterReference address; \
        frame.P0 = P0.value; \
        frame.P1 = P1.value; \
        frame.P2 = P2.value; \
        address.address = &frame; \
        object->methods->process_event.parameters( \
            object, find_script_function(object, name_##Name, 0.0f), address, 0.0f); \
    }

SCRIPT_ACTION_1(script_call_AISetPlayerAsEnemy, AISetPlayerAsEnemy, RefPlayerController, enemyController)
SCRIPT_ACTION_1(script_call_AcquireObject, AcquireObject, RefEInventoryItem, Item)
SCRIPT_ACTION_1(script_call_ActorEnteredVolume, ActorEnteredVolume, RefActor, Other)
SCRIPT_ACTION_3(script_call_ActorGotoState, ActorGotoState, RefActor, A, ScriptEventName, State, ScriptEventName, Label)
SCRIPT_ACTION_1(script_call_ActorLeavingVolume, ActorLeavingVolume, RefActor, Other)
SCRIPT_ACTION_2(script_call_AddAmbientDamage, AddAmbientDamage, int, Ambient, int, Fire)
SCRIPT_ACTION_1(script_call_AdjustScope, AdjustScope, RefEScopeAttachment, Scope)
SCRIPT_ACTION_1(script_call_AltComCancel, AltComCancel, ScriptBoolean, bPlaySound)
SCRIPT_ACTION_1(script_call_AltComReady, AltComReady, ScriptBoolean, bPlaySound)
SCRIPT_ACTION_1(script_call_AttachWeaponToHand, AttachWeaponToHand, ScriptBoolean, bAttachAwayBone)
SCRIPT_QUERY_0(script_call_CanAccessQuick, CanAccessQuick, ScriptBoolean)
SCRIPT_QUERY_1(script_call_CanAddInteract, CanAddInteract, ScriptBoolean, RefEInteractObject, IntObj)
SCRIPT_QUERY_1(script_call_CanAddThisItem, CanAddThisItem, ScriptBoolean, RefEInventoryItem, ItemToAdd)
SCRIPT_QUERY_0(script_call_CanInteract, CanInteract, ScriptBoolean)
SCRIPT_QUERY_0(script_call_CanLoadGame, CanLoadGame, ScriptBoolean)
SCRIPT_QUERY_0(script_call_CanPerformCloseInteraction, CanPerformCloseInteraction, ScriptBoolean)
SCRIPT_QUERY_0(script_call_CanPerformSwitchTransition, CanPerformSwitchTransition, ScriptBoolean)
SCRIPT_QUERY_0(script_call_CanSaveGame, CanSaveGame, ScriptBoolean)
SCRIPT_QUERY_0(script_call_CanSplash, CanSplash, ScriptBoolean)
SCRIPT_ACTION_2(script_call_ChangeHandObject, ChangeHandObject, RefEGameplayObject, Obj, ScriptBoolean, bSheathItem)
SCRIPT_QUERY_0(script_call_CheckForSettingOffFeetIK, CheckForSettingOffFeetIK, ScriptBoolean)
SCRIPT_ACTION_1(script_call_CheckRepeatFire, CheckRepeatFire, float, fDeltaSeconds)
SCRIPT_ACTION_1(script_call_Close, Close, ScriptBoolean, bInterrupted)
SCRIPT_ACTION_1(script_call_EndCrouch, EndCrouch, float, HeightAdjust)
SCRIPT_ACTION_2(script_call_EndMission, EndMission, ScriptBoolean, bMissionComplete, int, iFailureType)
SCRIPT_ACTION_2(script_call_EndMissionCoop, EndMissionCoop, ScriptBoolean, bMissionComplete, int, iFailureType)
SCRIPT_QUERY_0(script_call_EscapeView, EscapeView, ScriptBoolean)
SCRIPT_QUERY_0(script_call_ExploseAtTheEnd, ExploseAtTheEnd, ScriptBoolean)
SCRIPT_QUERY_1(script_call_FinishDoorOpening, FinishDoorOpening, ScriptBoolean, int, O)
SCRIPT_ACTION_1(script_call_Flash, Flash, float, duration)
SCRIPT_ACTION_1(script_call_Force2DIK, Force2DIK, ScriptBoolean, in_state)
SCRIPT_QUERY_1(script_call_GameFlowSetPause, GameFlowSetPause, ScriptBoolean, ScriptBoolean, bPause)
SCRIPT_QUERY_0(script_call_GetAttenuationFactor, GetAttenuationFactor, float)
SCRIPT_QUERY_0(script_call_GetCurrent, GetCurrent, RefEGoal)
SCRIPT_QUERY_0(script_call_GetCurrentFOV, GetCurrentFOV, float)
SCRIPT_QUERY_0(script_call_GetCurrentZoomLevel, GetCurrentZoomLevel, float)
SCRIPT_QUERY_0(script_call_GetFatigueLevel, GetFatigueLevel, float)
SCRIPT_QUERY_0(script_call_GetHealthPCT, GetHealthPCT, float)
SCRIPT_QUERY_0(script_call_GetHeightFactor, GetHeightFactor, float)
SCRIPT_QUERY_0(script_call_GetMaxFOV, GetMaxFOV, float)
SCRIPT_QUERY_0(script_call_GetMaximumAimAngle, GetMaximumAimAngle, float)
SCRIPT_QUERY_0(script_call_GetScope, GetScope, RefEScopeAttachment)
SCRIPT_ACTION_1(script_call_GoOnBed, GoOnBed, ScriptBoolean, bGoIntoBed)
SCRIPT_ACTION_1(script_call_GoOnChair, GoOnChair, ScriptBoolean, bGoIntoChair)
SCRIPT_ACTION_1(script_call_GoOnLadder, GoOnLadder, ScriptBoolean, bGoUp)
SCRIPT_ACTION_1(script_call_GotoStateSafe_111fa048_ScriptEventName, GotoStateSafe_slot_111fa048, ScriptEventName, State)
SCRIPT_ACTION_1(script_call_GotoStateSafe_1122f4f0_ScriptEventName, GotoStateSafe_slot_1122f4f0, ScriptEventName, State)
SCRIPT_ACTION_2(script_call_GotoStateSafe_1122f4f0_ScriptEventNameScriptEventName, GotoStateSafe_slot_1122f4f0, ScriptEventName, State, ScriptEventName, Label)
SCRIPT_ACTION_1(script_call_HudView, HudView, ScriptBoolean, bIn)
SCRIPT_ACTION_2(script_call_InitInventory, InitInventory, RefEPawn, Pawn, ScriptBoolean, bLoading)
SCRIPT_QUERY_1(script_call_IsExtraWaiting, IsExtraWaiting, ScriptBoolean, int, F)
SCRIPT_QUERY_0(script_call_IsGamePaused, IsGamePaused, ScriptBoolean)
SCRIPT_QUERY_0(script_call_IsJumpingForward, IsJumpingForward, ScriptBoolean)
SCRIPT_QUERY_1(script_call_IsOCPActive, IsOCPActive, ScriptBoolean, ScriptBoolean, bJammer)
SCRIPT_QUERY_0(script_call_IsPossibleContextualAction, IsPossibleContextualAction, ScriptBoolean)
SCRIPT_QUERY_0(script_call_IsSuspicious, IsSuspicious, ScriptBoolean)
SCRIPT_QUERY_0(script_call_IsValidContextualAction, IsValidContextualAction, ScriptBoolean)
SCRIPT_QUERY_0(script_call_IsWaiting, IsWaiting, ScriptBoolean)
SCRIPT_ACTION_1(script_call_LockOwner, LockOwner, ScriptBoolean, bLocked)
SCRIPT_ACTION_1(script_call_MakeGroupReturn, MakeGroupReturn, RefActor, Other)
SCRIPT_QUERY_1(script_call_NotifyPickup, NotifyPickup, ScriptBoolean, RefController, Instigator)
SCRIPT_QUERY_1(script_call_NotifyPrePickup, NotifyPrePickup, ScriptBoolean, RefController, Instigator)
SCRIPT_ACTION_1(script_call_NotifyShotJustMissed, NotifyShotJustMissed, RefPawn, Instigator)
SCRIPT_ACTION_1(script_call_NotifySwitchItem, NotifySwitchItem, RefEInventoryItem, NewItem)
SCRIPT_ACTION_1(script_call_NotifySwitchObject, NotifySwitchObject, RefActor, SwitchObject)
SCRIPT_ACTION_1(script_call_OtherTick, OtherTick, float, DeltaSeconds)
SCRIPT_QUERY_1(script_call_PauseGameNotSound, PauseGameNotSound, ScriptBoolean, ScriptBoolean, bPause)
SCRIPT_ACTION_1(script_call_PawnEnteredVolume, PawnEnteredVolume, RefPawn, P)
SCRIPT_ACTION_1(script_call_PawnLeavingVolume, PawnLeavingVolume, RefPawn, P)
SCRIPT_ACTION_1(script_call_PipeVToPipeH, PipeVToPipeH, float, locationEndZ)
SCRIPT_ACTION_1(script_call_PlayClaw, PlayClaw, ScriptBoolean, bPlay)
SCRIPT_ACTION_1(script_call_PlayCommBarks, PlayCommBarks, int, iType)
SCRIPT_ACTION_1(script_call_PlayLilRail, PlayLilRail, ScriptBoolean, bPlay)
SCRIPT_ACTION_1(script_call_PlayOneShot, PlayOneShot, RefActor, Act)
SCRIPT_ACTION_1(script_call_PlayRail, PlayRail, ScriptBoolean, bPlay)
SCRIPT_ACTION_1(script_call_PlayerInput, PlayerInput, float, DeltaTime)
SCRIPT_ACTION_1(script_call_PollingTick, PollingTick, float, DeltaSeconds)
SCRIPT_ACTION_1(script_call_PreCarrySetup, PreCarrySetup, ScriptBoolean, reverse)
SCRIPT_ACTION_1(script_call_Push, Push, ScriptBoolean, bShouldFade)
SCRIPT_ACTION_1(script_call_Recoil, Recoil, float, extraScale)
SCRIPT_ACTION_1(script_call_RemoveInteraction, RemoveInteraction, RefInteraction, RemoveMe)
SCRIPT_ACTION_1(script_call_Revive, Revive, RefPawn, reviver)
SCRIPT_ACTION_3(script_call_RumbleShake, RumbleShake, float, duration, float, Strenth, float, Buildup)
SCRIPT_ACTION_3(script_call_RumbleVibrate, RumbleVibrate, float, duration, float, Strenth, float, Buildup)
SCRIPT_ACTION_1(script_call_SelectWeapon, SelectWeapon, ScriptBoolean, bSelectOneHanded)
SCRIPT_ACTION_1(script_call_SendThrownOject, SendThrownOject, RefActor, Obj)
SCRIPT_ACTION_1(script_call_SetFeetIK, SetFeetIK, ScriptBoolean, in_activate)
SCRIPT_ACTION_2(script_call_SetHandIK, SetHandIK, ScriptBoolean, in_activate, float, in_fade)
SCRIPT_ACTION_1(script_call_SetHumanIK, SetHumanIK, ScriptBoolean, in_state)
SCRIPT_ACTION_1(script_call_SetPower, SetPower, ScriptBoolean, bOn)
SCRIPT_ACTION_2(script_call_SetVolumeZone, SetVolumeZone, ScriptBoolean, bEntering, RefEVolume, Volume)
SCRIPT_QUERY_1(script_call_SpecialCost, SpecialCost, int, RefPawn, Seeker)
SCRIPT_ACTION_1(script_call_SpecialTick, SpecialTick, float, DeltaTime)
SCRIPT_ACTION_1(script_call_StartCrouch, StartCrouch, float, HeightAdjust)
SCRIPT_QUERY_0(script_call_StepDampingFadeOut, StepDampingFadeOut, float)
SCRIPT_QUERY_1(script_call_SuggestMovePreparation, SuggestMovePreparation, ScriptBoolean, RefPawn, Other)
SCRIPT_QUERY_0(script_call_SwitchAttachment, SwitchAttachment, ScriptBoolean)
SCRIPT_QUERY_0(script_call_TakeHit, TakeHit, ScriptBoolean)
SCRIPT_ACTION_2(script_call_UnSelect, UnSelect, RefEInventory, Inv, ScriptBoolean, bSameClass)
SCRIPT_ACTION_1(script_call_UpdateDisplays, UpdateDisplays, int, FloorNumber)
SCRIPT_ACTION_2(script_call_ValidateCoopUser, ValidateCoopUser, RefPawn, Scanned, ScriptBoolean, primary)
SCRIPT_ACTION_1(script_call_ValidateUser, ValidateUser, RefPawn, Scanned)
SCRIPT_ACTION_1(script_call_ZoneChange, ZoneChange, RefZoneInfo, NewZone)
