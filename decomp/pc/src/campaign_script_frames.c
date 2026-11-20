#include "campaign_script_events.h"

typedef int ScriptBoolean;
typedef unsigned char ScriptByte;
typedef struct { float x, y, z; } ScriptVector;
typedef struct { int pitch, yaw, roll; } ScriptRotator;
typedef struct { ScriptByte b, g, r, a; } ScriptColor;
struct ScriptStateFrame {
    unsigned char frame_fields[0x20];
    unsigned __int64 probe_mask;
};
struct ScriptObjectState {
    struct ScriptObject object;
    void *object_fields[2];
    struct ScriptStateFrame *state;
};

typedef struct Script_Actor *RefActor;
typedef struct Script_Controller *RefController;
typedef struct Script_EGameplayObject *RefEGameplayObject;
typedef struct Script_EPawn *RefEPawn;
typedef struct Script_EWeaponAttachment *RefEWeaponAttachment;
typedef struct Script_Pawn *RefPawn;
typedef struct Script_PhysicsVolume *RefPhysicsVolume;
typedef struct Script_Sound *RefSound;
typedef struct Script_StaticMesh *RefStaticMesh;
typedef struct { RefActor value; } Arg_RefActor;
typedef struct { RefController value; } Arg_RefController;
typedef struct { RefEGameplayObject value; } Arg_RefEGameplayObject;
typedef struct { RefEPawn value; } Arg_RefEPawn;
typedef struct { RefEWeaponAttachment value; } Arg_RefEWeaponAttachment;
typedef struct { RefPawn value; } Arg_RefPawn;
typedef struct { RefPhysicsVolume value; } Arg_RefPhysicsVolume;
typedef struct { RefSound value; } Arg_RefSound;
typedef struct { RefStaticMesh value; } Arg_RefStaticMesh;
typedef struct { ScriptBoolean value; } Arg_ScriptBoolean;
typedef struct { ScriptByte value; } Arg_ScriptByte;
typedef struct { ScriptEventName value; } Arg_ScriptEventName;
typedef struct { ScriptRotator value; } Arg_ScriptRotator;
typedef struct { ScriptVector value; } Arg_ScriptVector;
typedef struct { float value; } Arg_float;
typedef struct { int value; } Arg_int;

extern ScriptEventName name_ActorEntered;
void __fastcall script_frame_ActorEntered(struct ScriptObject *object, Arg_RefActor Other)
{
    struct {
        RefActor Other;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 21)))
        return;
    frame.Other = Other.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_ActorEntered, 0.0f), address, 0.0f);
}

extern ScriptEventName name_ActorGotoState;
void __fastcall script_frame_ActorGotoState(struct ScriptObject *object, Arg_RefActor A, Arg_ScriptEventName newStateName, Arg_ScriptEventName newLabelName, Arg_ScriptEventName newJumpLabelName)
{
    struct {
        RefActor A;
        ScriptEventName newStateName;
        ScriptEventName newLabelName;
        ScriptEventName newJumpLabelName;
    } frame;
    ParameterReference address;
    frame.A = A.value;
    frame.newStateName = newStateName.value;
    frame.newLabelName = newLabelName.value;
    frame.newJumpLabelName = newJumpLabelName.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_ActorGotoState, 0.0f), address, 0.0f);
}

extern ScriptEventName name_ActorLeaving;
void __fastcall script_frame_ActorLeaving(struct ScriptObject *object, Arg_RefActor Other)
{
    struct {
        RefActor Other;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 22)))
        return;
    frame.Other = Other.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_ActorLeaving, 0.0f), address, 0.0f);
}

extern ScriptEventName name_AmmoContact;
void __fastcall script_frame_AmmoContact(struct ScriptObject *object, Arg_ScriptVector HitDirection, Arg_ScriptVector HitNormal, Arg_RefActor Hit)
{
    struct {
        ScriptVector HitDirection;
        ScriptVector HitNormal;
        RefActor Hit;
    } frame;
    ParameterReference address;
    frame.HitDirection = HitDirection.value;
    frame.HitNormal = HitNormal.value;
    frame.Hit = Hit.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_AmmoContact, 0.0f), address, 0.0f);
}

extern ScriptEventName name_AnimEnd;
void __fastcall script_frame_AnimEnd(struct ScriptObject *object, Arg_int Channel)
{
    struct {
        int Channel;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 24)))
        return;
    frame.Channel = Channel.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_AnimEnd, 0.0f), address, 0.0f);
}

extern ScriptEventName name_Attach;
void __fastcall script_frame_Attach(struct ScriptObject *object, Arg_RefActor Other)
{
    struct {
        RefActor Other;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 19)))
        return;
    frame.Other = Other.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_Attach, 0.0f), address, 0.0f);
}

extern ScriptEventName name_AttachObject;
void __fastcall script_frame_AttachObject(struct ScriptObject *object, Arg_RefEGameplayObject Obj, Arg_ScriptEventName AttachTag, Arg_int inDummyAttachIndex, Arg_int in_channel, Arg_ScriptEventName DASource, Arg_ScriptEventName DATarget, Arg_ScriptEventName DATransition, Arg_ScriptVector DATransitionRelPos, Arg_ScriptRotator DATransitionRelRot)
{
    struct {
        RefEGameplayObject Obj;
        ScriptEventName AttachTag;
        int inDummyAttachIndex;
        int in_channel;
        ScriptEventName DASource;
        ScriptEventName DATarget;
        ScriptEventName DATransition;
        ScriptVector DATransitionRelPos;
        ScriptRotator DATransitionRelRot;
    } frame;
    ParameterReference address;
    frame.Obj = Obj.value;
    frame.AttachTag = AttachTag.value;
    frame.inDummyAttachIndex = inDummyAttachIndex.value;
    frame.in_channel = in_channel.value;
    frame.DASource = DASource.value;
    frame.DATarget = DATarget.value;
    frame.DATransition = DATransition.value;
    frame.DATransitionRelPos = DATransitionRelPos.value;
    frame.DATransitionRelRot = DATransitionRelRot.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_AttachObject, 0.0f), address, 0.0f);
}

extern ScriptEventName name_BaseChange;
void __fastcall script_frame_BaseChange(struct ScriptObject *object)
{
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 18)))
        return;
    object->methods->process_event.empty(
        object, find_script_function(object, name_BaseChange, 0.0f), 0.0f, 0.0f);
}

extern ScriptEventName name_Bump;
void __fastcall script_frame_Bump(struct ScriptObject *object, Arg_RefActor Other, Arg_int Pill)
{
    struct {
        RefActor Other;
        int Pill;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 15)))
        return;
    frame.Other = Other.value;
    frame.Pill = Pill.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_Bump, 0.0f), address, 0.0f);
}

extern ScriptEventName name_CanUseOpenDoorType;
ScriptBoolean __fastcall script_frame_CanUseOpenDoorType(struct ScriptObject *object, Arg_ScriptByte openType)
{
    struct {
        ScriptByte openType;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.openType = openType.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_CanUseOpenDoorType, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_CollidedBy;
void __fastcall script_frame_CollidedBy(struct ScriptObject *object, Arg_RefActor Instigator, Arg_ScriptVector HitLocation)
{
    struct {
        RefActor Instigator;
        ScriptVector HitLocation;
    } frame;
    ParameterReference address;
    frame.Instigator = Instigator.value;
    frame.HitLocation = HitLocation.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_CollidedBy, 0.0f), address, 0.0f);
}

extern ScriptEventName name_Destroyed;
void __fastcall script_frame_Destroyed(struct ScriptObject *object)
{
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 1)))
        return;
    object->methods->process_event.empty(
        object, find_script_function(object, name_Destroyed, 0.0f), 0.0f, 0.0f);
}

extern ScriptEventName name_Detach;
void __fastcall script_frame_Detach(struct ScriptObject *object, Arg_RefActor Other)
{
    struct {
        RefActor Other;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 20)))
        return;
    frame.Other = Other.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_Detach, 0.0f), address, 0.0f);
}

extern ScriptEventName name_DoorOpenTypeIs;
void __fastcall script_frame_DoorOpenTypeIs(struct ScriptObject *object, Arg_ScriptByte openType, Arg_RefActor Door, Arg_ScriptVector vDoorMiddle)
{
    struct {
        ScriptByte openType;
        RefActor Door;
        ScriptVector vDoorMiddle;
    } frame;
    ParameterReference address;
    frame.openType = openType.value;
    frame.Door = Door.value;
    frame.vDoorMiddle = vDoorMiddle.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_DoorOpenTypeIs, 0.0f), address, 0.0f);
}

extern ScriptEventName name_EncroachedBy;
void __fastcall script_frame_EncroachedBy(struct ScriptObject *object, Arg_RefActor Other)
{
    struct {
        RefActor Other;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 28)))
        return;
    frame.Other = Other.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_EncroachedBy, 0.0f), address, 0.0f);
}

extern ScriptEventName name_EndedRotation;
void __fastcall script_frame_EndedRotation(struct ScriptObject *object)
{
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 25)))
        return;
    object->methods->process_event.empty(
        object, find_script_function(object, name_EndedRotation, 0.0f), 0.0f, 0.0f);
}

extern ScriptEventName name_Falling;
void __fastcall script_frame_Falling(struct ScriptObject *object)
{
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 10)))
        return;
    object->methods->process_event.empty(
        object, find_script_function(object, name_Falling, 0.0f), 0.0f, 0.0f);
}

extern ScriptEventName name_FeetLedge;
ScriptBoolean __fastcall script_frame_FeetLedge(struct ScriptObject *object, Arg_ScriptVector locationEnd, Arg_ScriptRotator orientationEnd)
{
    struct {
        ScriptVector locationEnd;
        ScriptRotator orientationEnd;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.locationEnd = locationEnd.value;
    frame.orientationEnd = orientationEnd.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_FeetLedge, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_GainedChild;
void __fastcall script_frame_GainedChild(struct ScriptObject *object, Arg_RefActor Other)
{
    struct {
        RefActor Other;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 2)))
        return;
    frame.Other = Other.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_GainedChild, 0.0f), address, 0.0f);
}

extern ScriptEventName name_GenerateAINoise;
void __fastcall script_frame_GenerateAINoise(struct ScriptObject *object, Arg_RefSound Snd, Arg_ScriptByte iAction, Arg_ScriptByte iSurface, Arg_float speed)
{
    struct {
        RefSound Snd;
        ScriptByte iAction;
        ScriptByte iSurface;
        float speed;
    } frame;
    ParameterReference address;
    frame.Snd = Snd.value;
    frame.iAction = iAction.value;
    frame.iSurface = iSurface.value;
    frame.speed = speed.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_GenerateAINoise, 0.0f), address, 0.0f);
}

extern ScriptEventName name_GetOpenDoorType;
ScriptByte __fastcall script_frame_GetOpenDoorType(struct ScriptObject *object, Arg_int iOutroZone)
{
    struct {
        int iOutroZone;
        ScriptByte result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.iOutroZone = iOutroZone.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_GetOpenDoorType, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_HandOverHand;
ScriptBoolean __fastcall script_frame_HandOverHand(struct ScriptObject *object, Arg_ScriptVector locationEnd, Arg_ScriptRotator orientationEnd)
{
    struct {
        ScriptVector locationEnd;
        ScriptRotator orientationEnd;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.locationEnd = locationEnd.value;
    frame.orientationEnd = orientationEnd.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_HandOverHand, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_HavokBump;
void __fastcall script_frame_HavokBump(struct ScriptObject *object, Arg_RefActor Other, Arg_ScriptVector hitVelocity, Arg_ScriptVector HitLocation)
{
    struct {
        RefActor Other;
        ScriptVector hitVelocity;
        ScriptVector HitLocation;
    } frame;
    ParameterReference address;
    frame.Other = Other.value;
    frame.hitVelocity = hitVelocity.value;
    frame.HitLocation = HitLocation.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_HavokBump, 0.0f), address, 0.0f);
}

extern ScriptEventName name_HitWall;
void __fastcall script_frame_HitWall(struct ScriptObject *object, Arg_ScriptVector HitNormal, Arg_RefActor HitWall)
{
    struct {
        ScriptVector HitNormal;
        RefActor HitWall;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 9)))
        return;
    frame.HitNormal = HitNormal.value;
    frame.HitWall = HitWall.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_HitWall, 0.0f), address, 0.0f);
}

extern ScriptEventName name_IsActiveView;
ScriptBoolean __fastcall script_frame_IsActiveView(struct ScriptObject *object, Arg_ScriptByte eNextPlayerView)
{
    struct {
        ScriptByte eNextPlayerView;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.eNextPlayerView = eNextPlayerView.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_IsActiveView, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_IsGranted;
ScriptBoolean __fastcall script_frame_IsGranted(struct ScriptObject *object, Arg_RefEPawn Pawn, Arg_ScriptBoolean skipObjectState)
{
    struct {
        RefEPawn Pawn;
        ScriptBoolean skipObjectState;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.Pawn = Pawn.value;
    frame.skipObjectState = skipObjectState.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_IsGranted, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_Landed;
void __fastcall script_frame_Landed(struct ScriptObject *object, Arg_ScriptVector HitNormal)
{
    struct {
        ScriptVector HitNormal;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 11)))
        return;
    frame.HitNormal = HitNormal.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_Landed, 0.0f), address, 0.0f);
}

extern ScriptEventName name_Ledge;
ScriptBoolean __fastcall script_frame_Ledge(struct ScriptObject *object, Arg_ScriptVector locationEnd, Arg_ScriptRotator orientationEnd, Arg_int LType)
{
    struct {
        ScriptVector locationEnd;
        ScriptRotator orientationEnd;
        int LType;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.locationEnd = locationEnd.value;
    frame.orientationEnd = orientationEnd.value;
    frame.LType = LType.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_Ledge, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_LostChild;
void __fastcall script_frame_LostChild(struct ScriptObject *object, Arg_RefActor Other)
{
    struct {
        RefActor Other;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 3)))
        return;
    frame.Other = Other.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_LostChild, 0.0f), address, 0.0f);
}

extern ScriptEventName name_MayFall;
void __fastcall script_frame_MayFall(struct ScriptObject *object)
{
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 33)))
        return;
    object->methods->process_event.empty(
        object, find_script_function(object, name_MayFall, 0.0f), 0.0f, 0.0f);
}

extern ScriptEventName name_NarrowGap;
ScriptBoolean __fastcall script_frame_NarrowGap(struct ScriptObject *object, Arg_ScriptVector locationEnd, Arg_ScriptRotator orientationEnd)
{
    struct {
        ScriptVector locationEnd;
        ScriptRotator orientationEnd;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.locationEnd = locationEnd.value;
    frame.orientationEnd = orientationEnd.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_NarrowGap, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_NarrowLadder;
ScriptBoolean __fastcall script_frame_NarrowLadder(struct ScriptObject *object, Arg_ScriptVector locationEnd, Arg_ScriptRotator orientationEnd, Arg_int NLType)
{
    struct {
        ScriptVector locationEnd;
        ScriptRotator orientationEnd;
        int NLType;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.locationEnd = locationEnd.value;
    frame.orientationEnd = orientationEnd.value;
    frame.NLType = NLType.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_NarrowLadder, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_NotifyEnterDoorRadius;
void __fastcall script_frame_NotifyEnterDoorRadius(struct ScriptObject *object, Arg_ScriptByte doorStatus)
{
    struct {
        ScriptByte doorStatus;
    } frame;
    ParameterReference address;
    frame.doorStatus = doorStatus.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_NotifyEnterDoorRadius, 0.0f), address, 0.0f);
}

extern ScriptEventName name_NotifyTouchWall;
void __fastcall script_frame_NotifyTouchWall(struct ScriptObject *object, Arg_ScriptVector HitNormal, Arg_RefActor wall)
{
    struct {
        ScriptVector HitNormal;
        RefActor wall;
    } frame;
    ParameterReference address;
    frame.HitNormal = HitNormal.value;
    frame.wall = wall.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_NotifyTouchWall, 0.0f), address, 0.0f);
}

extern ScriptEventName name_OCPEffect;
void __fastcall script_frame_OCPEffect(struct ScriptObject *object, Arg_ScriptVector Pos)
{
    struct {
        ScriptVector Pos;
    } frame;
    ParameterReference address;
    frame.Pos = Pos.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_OCPEffect, 0.0f), address, 0.0f);
}

extern ScriptEventName name_PauseGame;
ScriptBoolean __fastcall script_frame_PauseGame(struct ScriptObject *object, Arg_ScriptBoolean bPause, Arg_ScriptBoolean bPauseAllSounds)
{
    struct {
        ScriptBoolean bPause;
        ScriptBoolean bPauseAllSounds;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.bPause = bPause.value;
    frame.bPauseAllSounds = bPauseAllSounds.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_PauseGame, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_PerformingCoopInteract;
ScriptBoolean __fastcall script_frame_PerformingCoopInteract(struct ScriptObject *object, Arg_ScriptByte Action)
{
    struct {
        ScriptByte Action;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.Action = Action.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_PerformingCoopInteract, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_PhysicsVolumeChange;
void __fastcall script_frame_PhysicsVolumeChange(struct ScriptObject *object, Arg_RefPhysicsVolume NewVolume)
{
    struct {
        RefPhysicsVolume NewVolume;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 12)))
        return;
    frame.NewVolume = NewVolume.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_PhysicsVolumeChange, 0.0f), address, 0.0f);
}

extern ScriptEventName name_Pipe;
ScriptBoolean __fastcall script_frame_Pipe(struct ScriptObject *object, Arg_ScriptVector locationEnd, Arg_ScriptRotator orientationEnd)
{
    struct {
        ScriptVector locationEnd;
        ScriptRotator orientationEnd;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.locationEnd = locationEnd.value;
    frame.orientationEnd = orientationEnd.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_Pipe, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_PlayAltComMove;
void __fastcall script_frame_PlayAltComMove(struct ScriptObject *object, Arg_ScriptBoolean forward, Arg_ScriptBoolean back, Arg_ScriptBoolean Left, Arg_ScriptBoolean Right, Arg_ScriptBoolean raise, Arg_ScriptBoolean lower, Arg_ScriptBoolean Stop)
{
    struct {
        ScriptBoolean forward;
        ScriptBoolean back;
        ScriptBoolean Left;
        ScriptBoolean Right;
        ScriptBoolean raise;
        ScriptBoolean lower;
        ScriptBoolean Stop;
    } frame;
    ParameterReference address;
    frame.forward = forward.value;
    frame.back = back.value;
    frame.Left = Left.value;
    frame.Right = Right.value;
    frame.raise = raise.value;
    frame.lower = lower.value;
    frame.Stop = Stop.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_PlayAltComMove, 0.0f), address, 0.0f);
}

extern ScriptEventName name_PlayAnimNoMovement;
ScriptBoolean __fastcall script_frame_PlayAnimNoMovement(struct ScriptObject *object, Arg_ScriptEventName Sequence, Arg_float Rate, Arg_float TweenTime, Arg_ScriptBoolean bBackward)
{
    struct {
        ScriptEventName Sequence;
        float Rate;
        float TweenTime;
        ScriptBoolean bBackward;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.Sequence = Sequence.value;
    frame.Rate = Rate.value;
    frame.TweenTime = TweenTime.value;
    frame.bBackward = bBackward.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_PlayAnimNoMovement, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_PlayRandom;
int __fastcall script_frame_PlayRandom(struct ScriptObject *object, Arg_RefActor Owner, Arg_float Delay)
{
    struct {
        RefActor Owner;
        float Delay;
        int result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.Owner = Owner.value;
    frame.Delay = Delay.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_PlayRandom, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_PlayerTick;
void __fastcall script_frame_PlayerTick(struct ScriptObject *object, Arg_float DeltaTime)
{
    struct {
        float DeltaTime;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 37)))
        return;
    frame.DeltaTime = DeltaTime.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_PlayerTick, 0.0f), address, 0.0f);
}

extern ScriptEventName name_Poutre;
ScriptBoolean __fastcall script_frame_Poutre(struct ScriptObject *object, Arg_ScriptVector locationEnd, Arg_ScriptRotator orientationEnd, Arg_int PType)
{
    struct {
        ScriptVector locationEnd;
        ScriptRotator orientationEnd;
        int PType;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.locationEnd = locationEnd.value;
    frame.orientationEnd = orientationEnd.value;
    frame.PType = PType.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_Poutre, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_ReceiveMessage;
void __fastcall script_frame_ReceiveMessage(struct ScriptObject *object, Arg_RefEGameplayObject Sender, Arg_ScriptByte Event)
{
    struct {
        RefEGameplayObject Sender;
        ScriptByte Event;
    } frame;
    ParameterReference address;
    frame.Sender = Sender.value;
    frame.Event = Event.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_ReceiveMessage, 0.0f), address, 0.0f);
}

extern ScriptEventName name_ReserveLocation;
void __fastcall script_frame_ReserveLocation(struct ScriptObject *object, Arg_ScriptVector vPos, Arg_ScriptBoolean bDontBlockPlayer)
{
    struct {
        ScriptVector vPos;
        ScriptBoolean bDontBlockPlayer;
    } frame;
    ParameterReference address;
    frame.vPos = vPos.value;
    frame.bDontBlockPlayer = bDontBlockPlayer.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_ReserveLocation, 0.0f), address, 0.0f);
}

extern ScriptEventName name_ResetAttachment;
void __fastcall script_frame_ResetAttachment(struct ScriptObject *object, Arg_RefEWeaponAttachment Attachment, Arg_ScriptVector AttachmentOffset)
{
    struct {
        RefEWeaponAttachment Attachment;
        ScriptVector AttachmentOffset;
    } frame;
    ParameterReference address;
    frame.Attachment = Attachment.value;
    frame.AttachmentOffset = AttachmentOffset.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_ResetAttachment, 0.0f), address, 0.0f);
}

extern ScriptEventName name_RestoreView;
ScriptBoolean __fastcall script_frame_RestoreView(struct ScriptObject *object, Arg_ScriptByte eFROMPlayerView)
{
    struct {
        ScriptByte eFROMPlayerView;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.eFROMPlayerView = eFROMPlayerView.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_RestoreView, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_SetDistanceFog;
void __fastcall script_frame_SetDistanceFog(struct ScriptObject *object, Arg_float _fStartDist, Arg_float _fEndDist, Arg_float _fDensity, Arg_float _fTargetDelay)
{
    struct {
        float _fStartDist;
        float _fEndDist;
        float _fDensity;
        float _fTargetDelay;
    } frame;
    ParameterReference address;
    frame._fStartDist = _fStartDist.value;
    frame._fEndDist = _fEndDist.value;
    frame._fDensity = _fDensity.value;
    frame._fTargetDelay = _fTargetDelay.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_SetDistanceFog, 0.0f), address, 0.0f);
}

extern ScriptEventName name_SetDoorInteractionLocation;
void __fastcall script_frame_SetDoorInteractionLocation(struct ScriptObject *object, Arg_ScriptByte openType)
{
    struct {
        ScriptByte openType;
    } frame;
    ParameterReference address;
    frame.openType = openType.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_SetDoorInteractionLocation, 0.0f), address, 0.0f);
}

extern ScriptEventName name_SetRelativeOffset;
void __fastcall script_frame_SetRelativeOffset(struct ScriptObject *object, Arg_ScriptVector offset)
{
    struct {
        ScriptVector offset;
    } frame;
    ParameterReference address;
    frame.offset = offset.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_SetRelativeOffset, 0.0f), address, 0.0f);
}

extern ScriptEventName name_SetupRelative;
void __fastcall script_frame_SetupRelative(struct ScriptObject *object, Arg_RefActor NewBase, Arg_ScriptVector offset, Arg_RefStaticMesh Mesh)
{
    struct {
        RefActor NewBase;
        ScriptVector offset;
        RefStaticMesh Mesh;
    } frame;
    ParameterReference address;
    frame.NewBase = NewBase.value;
    frame.offset = offset.value;
    frame.Mesh = Mesh.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_SetupRelative, 0.0f), address, 0.0f);
}

extern ScriptEventName name_TakeDamage;
void __fastcall script_frame_TakeDamage(struct ScriptObject *object, Arg_int Damage, Arg_RefPawn EventInstigator, Arg_ScriptVector HitLocation, Arg_ScriptVector HitNormal, Arg_ScriptVector Momentum, Arg_ScriptByte DamageType, Arg_int PillTag)
{
    struct {
        int Damage;
        RefPawn EventInstigator;
        ScriptVector HitLocation;
        ScriptVector HitNormal;
        ScriptVector Momentum;
        ScriptByte DamageType;
        int PillTag;
    } frame;
    ParameterReference address;
    frame.Damage = Damage.value;
    frame.EventInstigator = EventInstigator.value;
    frame.HitLocation = HitLocation.value;
    frame.HitNormal = HitNormal.value;
    frame.Momentum = Momentum.value;
    frame.DamageType = DamageType.value;
    frame.PillTag = PillTag.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_TakeDamage, 0.0f), address, 0.0f);
}

extern ScriptEventName name_Throw;
void __fastcall script_frame_Throw(struct ScriptObject *object, Arg_RefController Thrower, Arg_ScriptVector ThrowVelocity)
{
    struct {
        RefController Thrower;
        ScriptVector ThrowVelocity;
    } frame;
    ParameterReference address;
    frame.Thrower = Thrower.value;
    frame.ThrowVelocity = ThrowVelocity.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_Throw, 0.0f), address, 0.0f);
}

extern ScriptEventName name_Tick;
void __fastcall script_frame_Tick(struct ScriptObject *object, Arg_float Delta)
{
    struct {
        float Delta;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 36)))
        return;
    frame.Delta = Delta.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_Tick, 0.0f), address, 0.0f);
}

extern ScriptEventName name_Timer;
void __fastcall script_frame_Timer(struct ScriptObject *object)
{
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 8)))
        return;
    object->methods->process_event.empty(
        object, find_script_function(object, name_Timer, 0.0f), 0.0f, 0.0f);
}

extern ScriptEventName name_TopNarrowLadder;
ScriptBoolean __fastcall script_frame_TopNarrowLadder(struct ScriptObject *object, Arg_ScriptVector locationEnd, Arg_ScriptRotator orientationEnd)
{
    struct {
        ScriptVector locationEnd;
        ScriptRotator orientationEnd;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.locationEnd = locationEnd.value;
    frame.orientationEnd = orientationEnd.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_TopNarrowLadder, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_TopPipe;
ScriptBoolean __fastcall script_frame_TopPipe(struct ScriptObject *object, Arg_ScriptVector locationEnd, Arg_ScriptRotator orientationEnd)
{
    struct {
        ScriptVector locationEnd;
        ScriptRotator orientationEnd;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.locationEnd = locationEnd.value;
    frame.orientationEnd = orientationEnd.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_TopPipe, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_Touch;
void __fastcall script_frame_Touch(struct ScriptObject *object, Arg_RefActor Other)
{
    struct {
        RefActor Other;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 13)))
        return;
    frame.Other = Other.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_Touch, 0.0f), address, 0.0f);
}

extern ScriptEventName name_Trigger;
void __fastcall script_frame_Trigger(struct ScriptObject *object, Arg_RefActor Other, Arg_RefPawn EventInstigator, Arg_ScriptEventName EventID)
{
    struct {
        RefActor Other;
        RefPawn EventInstigator;
        ScriptEventName EventID;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 6)))
        return;
    frame.Other = Other.value;
    frame.EventInstigator = EventInstigator.value;
    frame.EventID = EventID.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_Trigger, 0.0f), address, 0.0f);
}

extern ScriptEventName name_TriggerEvent;
void __fastcall script_frame_TriggerEvent(struct ScriptObject *object, Arg_ScriptEventName EventName, Arg_RefActor Other, Arg_RefPawn EventInstigator, Arg_ScriptEventName EventID)
{
    struct {
        ScriptEventName EventName;
        RefActor Other;
        RefPawn EventInstigator;
        ScriptEventName EventID;
    } frame;
    ParameterReference address;
    frame.EventName = EventName.value;
    frame.Other = Other.value;
    frame.EventInstigator = EventInstigator.value;
    frame.EventID = EventID.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_TriggerEvent, 0.0f), address, 0.0f);
}

extern ScriptEventName name_TurnOn;
void __fastcall script_frame_TurnOn(struct ScriptObject *object, Arg_ScriptByte Type, Arg_RefPawn EventInstigator)
{
    struct {
        ScriptByte Type;
        RefPawn EventInstigator;
    } frame;
    ParameterReference address;
    frame.Type = Type.value;
    frame.EventInstigator = EventInstigator.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_TurnOn, 0.0f), address, 0.0f);
}

extern ScriptEventName name_UnTouch;
void __fastcall script_frame_UnTouch(struct ScriptObject *object, Arg_RefActor Other)
{
    struct {
        RefActor Other;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 14)))
        return;
    frame.Other = Other.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_UnTouch, 0.0f), address, 0.0f);
}

extern ScriptEventName name_UnTrigger;
void __fastcall script_frame_UnTrigger(struct ScriptObject *object, Arg_RefActor Other, Arg_RefPawn EventInstigator)
{
    struct {
        RefActor Other;
        RefPawn EventInstigator;
    } frame;
    ParameterReference address;
    const struct ScriptStateFrame *state = ((const struct ScriptObjectState *)object)->state;
    if (state && !(state->probe_mask & (1ui64 << 7)))
        return;
    frame.Other = Other.value;
    frame.EventInstigator = EventInstigator.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_UnTrigger, 0.0f), address, 0.0f);
}

extern ScriptEventName name_WaistLedge;
ScriptBoolean __fastcall script_frame_WaistLedge(struct ScriptObject *object, Arg_ScriptVector locationEnd, Arg_ScriptRotator orientationEnd)
{
    struct {
        ScriptVector locationEnd;
        ScriptRotator orientationEnd;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.locationEnd = locationEnd.value;
    frame.orientationEnd = orientationEnd.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_WaistLedge, 0.0f), address, 0.0f);
    return frame.result;
}

extern ScriptEventName name_ZipLine;
ScriptBoolean __fastcall script_frame_ZipLine(struct ScriptObject *object, Arg_ScriptVector locationEnd, Arg_ScriptRotator orientationEnd, Arg_int ZLType)
{
    struct {
        ScriptVector locationEnd;
        ScriptRotator orientationEnd;
        int ZLType;
        ScriptBoolean result;
    } frame;
    ParameterReference address;
    frame.result = 0;
    frame.locationEnd = locationEnd.value;
    frame.orientationEnd = orientationEnd.value;
    frame.ZLType = ZLType.value;
    address.address = &frame;
    object->methods->process_event.parameters(
        object, find_script_function(object, name_ZipLine, 0.0f), address, 0.0f);
    return frame.result;
}
