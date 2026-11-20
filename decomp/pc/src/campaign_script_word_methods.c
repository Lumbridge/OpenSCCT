#include "campaign_vm_frame.h"

/* Script values occupy four-byte stack arguments in these native calls. */
struct Word { unsigned value; };

#define SCRIPT_METHOD_1(name) \
    extern void __fastcall native_##name(Object *, struct Word); \
    HANDLER(exec_##name, void) \
    { \
        struct Word argument = {0}; \
        step(frame, &argument); \
        finish_arguments(frame); \
        native_##name(self, argument); \
    }

#define SCRIPT_METHOD_2(name) \
    extern void __fastcall native_##name(Object *, struct Word, struct Word); \
    HANDLER(exec_##name, void) \
    { \
        struct Word first = {0}, second; \
        step(frame, &first); \
        second.value = 0; \
        step(frame, &second); \
        finish_arguments(frame); \
        native_##name(self, first, second); \
    }

SCRIPT_METHOD_1(Engine_EInteractionManager_RemoveInteractionObj)
SCRIPT_METHOD_1(Engine_EInteractionManager_AddInteractionObj)
SCRIPT_METHOD_1(Engine_PlayerController_StopRenderWorld)
SCRIPT_METHOD_1(Engine_ESList_Remove)
SCRIPT_METHOD_1(Engine_ESList_InsertAtEnd)
SCRIPT_METHOD_1(AActor_SetCollision)
SCRIPT_METHOD_1(AActor_SetDrawScale)
SCRIPT_METHOD_1(AActor_SetOwner)
SCRIPT_METHOD_1(Engine_Pawn_ValidateFeetIK)
SCRIPT_METHOD_1(Engine_Pawn_Crouch)
SCRIPT_METHOD_1(Engine_Pawn_UnCrouch)
SCRIPT_METHOD_1(Engine_Actor_SetStaticMesh)
SCRIPT_METHOD_1(Engine_Actor_SetCollisionPrim)
SCRIPT_METHOD_1(Echelon_EPawn_RollPawn)
SCRIPT_METHOD_1(Echelon_EPawn_ExitRootMotion)
SCRIPT_METHOD_1(Echelon_EPawn_DropAttachedItems)
SCRIPT_METHOD_1(Echelon_EPawn_ShowFlashlight)
SCRIPT_METHOD_1(Echelon_EPattern_GoToNextBehavior)
SCRIPT_METHOD_1(Echelon_EAIController_AddEnemyInfo)
SCRIPT_METHOD_1(Echelon_EPlayerController_PlayOnGround)
SCRIPT_METHOD_1(Echelon_EPlayerController_RefreshHumanIK)
SCRIPT_METHOD_1(Echelon_EPlayerController_PopCamera)
SCRIPT_METHOD_1(Echelon_EAIManager_DoSomethingIfAnotherNPCIsOnTheSameEvent)
SCRIPT_METHOD_1(Echelon_EChangedActorManager_CheckAcknowledgeList)
SCRIPT_METHOD_1(Echelon_EDoorOpener_TriggerLinkedActors)
SCRIPT_METHOD_1(Echelon_EFlashLight_ToggleLight)
SCRIPT_METHOD_1(Echelon_EchelonLevelInfo_UnlockLadder)
SCRIPT_METHOD_1(Echelon_EInventory_SetSelectedItem)
SCRIPT_METHOD_1(Echelon_EInventory_AddInventoryItem)
SCRIPT_METHOD_1(Echelon_EMissionManager_SetupPlayer)
SCRIPT_METHOD_1(EchelonIngredient_EKeyPad_ShowDoorCode)
SCRIPT_METHOD_1(EchelonIngredient_ESprinkler_Activate)
SCRIPT_METHOD_2(Engine_EAIZone_EnableZone)
SCRIPT_METHOD_2(Engine_ESList_InsertBefore)
SCRIPT_METHOD_2(Engine_Pawn_SetIKCompFlag)
SCRIPT_METHOD_2(Engine_Pawn_UnProne)
SCRIPT_METHOD_2(Engine_Actor_PauseAnim)
SCRIPT_METHOD_2(Echelon_EPattern_KillNPC)
SCRIPT_METHOD_2(Echelon_EPattern_RetrieveStack)
SCRIPT_METHOD_2(Echelon_EChangedActorManager_AddToAcknowledgeList)
SCRIPT_METHOD_2(Echelon_EChangedActorManager_RemoveFromAcknowledgeList)
SCRIPT_METHOD_2(Echelon_EChangedActorManager_RemoveFromAllUsersAcknowledgeList)
SCRIPT_METHOD_2(Echelon_EChangedActorManager_SyncAcknowledgedList)
SCRIPT_METHOD_2(Echelon_EPowerBox_SwitchPower)
SCRIPT_METHOD_2(Echelon_ECoopInteractDispatch_SpawnInteraction)
SCRIPT_METHOD_2(EchelonIngredient_ESprinkler_CheckTouching)
