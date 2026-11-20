typedef int SoundFixed;
typedef struct { SoundFixed x, y, z; } SoundVector;
typedef struct { SoundFixed nearDistance, maxDistance, fadeStart, fadeEnd; } SoundRollOff;

typedef struct Receiver {
    SoundVector position;
    unsigned char unknown0c[0x24];
    int disabled;
    int unknown34;
    struct Receiver *next;
} Receiver;
typedef struct { Receiver *receiver; int enabled; } ReceiverBinding;
typedef struct {
    unsigned char unknown00[0xc];
    SoundVector position;
    unsigned char unknown18[0xc];
    int flags;
    int bindingCount;
    unsigned char unknown2c[0x68];
    /* Only the binding count and entry stride are known for this trailing view. */
    ReceiverBinding bindings[];
} Parameters;
extern int SoundReceiverCount;
extern Receiver *FirstSoundReceiver;
extern SoundFixed __stdcall SND_fn_rPseudoNormeVectorSnd(const SoundVector *v);
extern SoundFixed __stdcall SND_fn_rDistanceToVolume(SoundFixed distance);
extern SoundFixed __stdcall SND_fn_rDistanceToVolumeEx(SoundFixed distance, const SoundRollOff *settings);

#pragma code_seg(".text$B")
SoundFixed __stdcall SND_fn_rGetNormeSxd(const Parameters *parameters)
{
    SoundFixed best = -96 * 65536;
    Receiver *receiver;
    SoundVector delta;

    if (parameters->flags & 1)
        return 0;
    if (SoundReceiverCount) {
        for (receiver = FirstSoundReceiver; receiver; receiver = receiver->next) {
            SoundFixed level = -96 * 65536;
            if (!receiver->disabled) {
                int i;
                for (i = 0; i < parameters->bindingCount; ++i)
                    if (parameters->bindings[i].receiver == receiver)
                        break;
                if (i < parameters->bindingCount && parameters->bindings[i].enabled) {
                    SoundFixed distance;
                    delta.x = parameters->position.x - receiver->position.x;
                    delta.y = parameters->position.y - receiver->position.y;
                    delta.z = parameters->position.z - receiver->position.z;
                    distance = SND_fn_rPseudoNormeVectorSnd(&delta);
                    if (parameters->flags & 4) {
                        const unsigned char *fields = (const unsigned char *)parameters;
                        level = SND_fn_rDistanceToVolumeEx(distance, (const SoundRollOff *)(fields + 0x9c));
                    } else {
                        level = SND_fn_rDistanceToVolume(distance);
                    }
                }
            }
            if (best < level)
                best = level;
        }
    }
    return best;
}
