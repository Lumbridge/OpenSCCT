typedef int SoundFixed;
typedef struct {
    SoundFixed nearDistance, maxDistance, fadeStart, fadeEnd;
} SoundRollOff;
extern SoundRollOff DefaultRollOff;
extern SoundFixed __stdcall SND_fn_rDistanceToVolumeEx(SoundFixed distance, const SoundRollOff *parameters);

#pragma code_seg(".text$B")
SoundFixed __stdcall SND_fn_rDistanceToVolume(SoundFixed distance)
{
    SoundRollOff settings = DefaultRollOff;
    return SND_fn_rDistanceToVolumeEx(distance, &settings);
}
