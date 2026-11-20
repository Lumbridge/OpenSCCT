typedef int Fixed;
typedef struct { Fixed x, y, z; } Vector;
typedef struct { Fixed nearDistance, maxDistance, fadeStart, fadeEnd; } RollOff;
typedef struct Receiver {
    Vector position, velocity, forward, right;
    int disabled;
} Receiver;
typedef struct { Receiver *receiver; int enabled; } Binding;
typedef struct {
    Fixed pitch, volume;
    unsigned char pan;
    unsigned char reserved[3];
    Vector position, velocity;
    int flags, bindingCount;
    unsigned char fields2c[0x68];
    Binding bindings[];
} Input;
typedef struct {
    Fixed pitch, volume;
    int mode;
    union { struct { unsigned char pan, dolby; } channels; int angle; } output;
} Mixer;
extern Receiver *FirstSoundReceiver;
extern Fixed __stdcall SND_fn_rPseudoNormeVectorSnd(const Vector *v);
extern Fixed __stdcall SND_fn_rDistanceToVolume(Fixed distance);
extern Fixed __stdcall SND_fn_rDistanceToVolumeEx(Fixed distance, const RollOff *settings);
extern unsigned char __stdcall SND_fn_ucPositionToPan(const Vector *source, const Vector *listener, const Vector *forward, const Vector *right);
extern unsigned char __stdcall SND_fn_ucPositionToDolby(const Vector *source, const Vector *listener, const Vector *forward);
extern Fixed __stdcall SND_fn_rDopplerPitch(const Vector *source, const Vector *sourceVelocity, const Vector *listener, const Vector *listenerVelocity);
extern Fixed __stdcall SND_fn_rMulRealRealSnd(Fixed a, Fixed b);
extern float __fastcall direction_in_turns(const Vector *position);

static __forceinline void from_receiver(const Input *input, Mixer *mix, Receiver *receiver)
{
    int i, angle;
    unsigned char pan, dolby;
    Fixed gain, pitchRatio;

    pan = 64;
    dolby = 0;
    gain = 0;
    pitchRatio = 65536;
    angle = 0;

    if (!receiver->disabled) {
        for (i = 0; i < input->bindingCount; ++i)
            if (input->bindings[i].receiver == receiver)
                break;
        if (i < input->bindingCount && input->bindings[i].enabled) {
            Vector delta;
            Fixed distance;
            delta.x = input->position.x - receiver->position.x;
            delta.y = input->position.y - receiver->position.y;
            delta.z = input->position.z - receiver->position.z;
            distance = SND_fn_rPseudoNormeVectorSnd(&delta);
            if (input->flags & 4)
                gain = SND_fn_rDistanceToVolumeEx(distance, (const RollOff *)((const char *)input + 0x9c));
            else
                gain = SND_fn_rDistanceToVolume(distance);
            switch (mix->mode) {
            case 0:
                pan = SND_fn_ucPositionToPan(&input->position, &receiver->position, &receiver->forward, &receiver->right);
                break;
            case 1:
                pan = SND_fn_ucPositionToPan(&input->position, &receiver->position, &receiver->forward, &receiver->right);
                dolby = SND_fn_ucPositionToDolby(&input->position, &receiver->position, &receiver->forward);
                break;
            case 2:
                angle = (int)(360.0f * direction_in_turns(&input->position));
                break;
            }
            if (!(input->flags & 2))
                pitchRatio = SND_fn_rDopplerPitch(&input->position, &input->velocity, &receiver->position, &receiver->velocity);
        }
    }
    mix->pitch = SND_fn_rMulRealRealSnd(pitchRatio, input->pitch);
    mix->volume = input->volume + gain;
    if (mix->volume < -96 * 65536)
        mix->volume = -96 * 65536;
    switch (mix->mode) {
    case 0: mix->output.channels.pan = pan; break;
    case 1: mix->output.channels.pan = pan; mix->output.channels.dolby = dolby; break;
    case 2: mix->output.angle = angle; break;
    }
}

#pragma code_seg(".text$V75c650")
void __cdecl spatial_parameters(const Input *input, Mixer *mix)
{
    from_receiver(input, mix, FirstSoundReceiver);
}
