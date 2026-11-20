typedef int SoundFixed;
typedef struct {
    SoundFixed nearDistance, maxDistance, fadeStart, fadeEnd;
} SoundRollOff;
extern SoundRollOff DefaultRollOff;

#pragma code_seg(".text$B")
void __stdcall SND_fn_vSetDefaultRollOff(const SoundRollOff *value)
{
    SoundFixed fadeEnd;

    if (value->nearDistance != -2)
        DefaultRollOff.nearDistance = value->nearDistance;
    else
        DefaultRollOff.nearDistance = 32768;
    DefaultRollOff.maxDistance = value->maxDistance == -2 ? -1 : value->maxDistance;
    if (DefaultRollOff.maxDistance < DefaultRollOff.nearDistance)
        DefaultRollOff.maxDistance = -1;
    DefaultRollOff.fadeStart = value->fadeStart;
    if (DefaultRollOff.fadeStart == -2)
        DefaultRollOff.fadeStart = -1;

    fadeEnd = value->fadeEnd;
    DefaultRollOff.fadeEnd = fadeEnd;
    if (fadeEnd == -2)
        DefaultRollOff.fadeEnd = -1;
}
