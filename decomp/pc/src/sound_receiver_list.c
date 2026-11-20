typedef struct { int x, y, z; } SoundVector;
typedef struct { SoundVector position, vector1, vector2, vector3; } ReceiverPose;
typedef struct Receiver Receiver;
typedef struct { Receiver *first, *last; int count; } ReceiverList;
struct Receiver {
    ReceiverPose pose;
    int disabled;
    int unknown34;
    Receiver *next, *prev;
    ReceiverList *list;
};
extern ReceiverList SoundReceivers;
extern int SoundReceiverCount;
extern void *__stdcall SND_fn_pvMallocSnd(int bytes);
extern void __stdcall SND_fn_vFreeSnd(void *buffer);
extern void __stdcall SND_fn_vMemCopySnd(void *destination, const void *source, int bytes);

#pragma code_seg(".text$B")
int __cdecl reset_sound_receivers(const void *unused)
{
    SoundReceiverCount = 0;
    SoundReceivers.first = 0;
    SoundReceivers.last = 0;
    SoundReceivers.count = 0;
    return 1;
}
static __forceinline void copy_receiver_pose(ReceiverPose *out, const ReceiverPose *in)
{
    SND_fn_vMemCopySnd(&out->position, &in->position, sizeof(SoundVector));
    SND_fn_vMemCopySnd(&out->vector1, &in->vector1, sizeof(SoundVector));
    SND_fn_vMemCopySnd(&out->vector2, &in->vector2, sizeof(SoundVector));
    SND_fn_vMemCopySnd(&out->vector3, &in->vector3, sizeof(SoundVector));
}
#pragma code_seg(".text$C")
Receiver *__cdecl create_sound_receiver(const ReceiverPose *pose)
{
    Receiver *receiver = SND_fn_pvMallocSnd(sizeof(Receiver));
    receiver->prev = 0;
    receiver->list = 0;
    receiver->next = 0;
    receiver->prev = SoundReceivers.last;
    receiver->list = &SoundReceivers;
    if (!SoundReceivers.first)
        SoundReceivers.first = receiver;
    else
        SoundReceivers.last->next = receiver;
    SoundReceivers.last = receiver;
    ++SoundReceivers.count;
    if (!pose) {
        ++SoundReceiverCount;
        receiver->disabled = 1;
    } else {
        copy_receiver_pose(&receiver->pose, pose);
        ++SoundReceiverCount;
    }
    return receiver;
}
#pragma code_seg(".text$E")
void __cdecl update_sound_receiver(Receiver *target, const ReceiverPose *pose)
{
    Receiver *receiver;
    for (receiver = SoundReceivers.first; receiver; receiver = receiver->next)
        if (receiver == target)
            break;
    if (receiver) {
        receiver->disabled = 0;
        copy_receiver_pose(&receiver->pose, pose);
    }
}
#pragma code_seg(".text$F")
void __cdecl destroy_sound_receiver(Receiver *receiver)
{
    if (!receiver)
        return;
    if (receiver->next)
        receiver->next->prev = receiver->prev;
    if (receiver->prev)
        receiver->prev->next = receiver->next;
    if (receiver->list) {
        --receiver->list->count;
        if (receiver->list->first == receiver)
            receiver->list->first = receiver->next;
        if (receiver->list->last == receiver)
            receiver->list->last = receiver->prev;
    }
    receiver->next = 0;
    receiver->prev = 0;
    receiver->list = 0;
    SND_fn_vFreeSnd(receiver);
    --SoundReceiverCount;
}
