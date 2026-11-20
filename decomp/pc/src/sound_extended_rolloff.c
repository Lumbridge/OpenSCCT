typedef int SoundFixed;
typedef struct {
    SoundFixed nearDistance, maxDistance, fadeStart, fadeEnd;
} SoundRollOff;
extern SoundRollOff DefaultRollOff;
extern void __stdcall SND_fn_vMemCopySnd(void *to, const void *from, int count);
extern SoundFixed __stdcall SND_fn_rDivRealRealSnd(SoundFixed numerator, SoundFixed denominator);
extern SoundFixed __stdcall SND_fn_rMulRealRealSnd(SoundFixed a, SoundFixed b);
extern SoundFixed __cdecl distance_attenuation(SoundFixed distance, SoundFixed nearDistance);
extern SoundFixed __cdecl volume_to_linear(SoundFixed volume, int bits);
extern SoundFixed __cdecl linear_to_volume(SoundFixed amplitude, int mode);

static __forceinline SoundFixed evaluate_rolloff(SoundFixed distance, SoundRollOff *resolved)
{
    if (resolved->maxDistance != -1 && distance > resolved->maxDistance)
        distance = resolved->maxDistance;
    if (distance < resolved->nearDistance)
        return 0;
    if (resolved->fadeStart != -1 && resolved->fadeEnd != -1 && distance >= resolved->fadeStart) {
        SoundFixed baseVolume, phase;
        if (distance < resolved->fadeEnd) {
            baseVolume = distance_attenuation(resolved->fadeStart, resolved->nearDistance);
            baseVolume = volume_to_linear(baseVolume, 8);
            baseVolume = SND_fn_rDivRealRealSnd(baseVolume, 128);
            phase = SND_fn_rDivRealRealSnd(distance - resolved->fadeStart, resolved->fadeEnd - resolved->fadeStart);
            phase = 65536 - phase;
            phase = SND_fn_rMulRealRealSnd(phase, baseVolume);
            return linear_to_volume(phase, 0);
        }
        return -96 * 65536;
    }
    return distance_attenuation(distance, resolved->nearDistance);
}

#pragma code_seg(".text$V74b540")
SoundFixed __stdcall SND_fn_rDistanceToVolumeEx(SoundFixed distance, const SoundRollOff *settings)
{
    SoundRollOff resolved;
    if (!settings) {
        SoundRollOff defaults = DefaultRollOff;
        return SND_fn_rDistanceToVolumeEx(distance, &defaults);
    }
    SND_fn_vMemCopySnd(&resolved, settings, sizeof(resolved));
    if (resolved.nearDistance == -2)
        resolved.nearDistance = DefaultRollOff.nearDistance;
    if (resolved.maxDistance == -2)
        resolved.maxDistance = DefaultRollOff.maxDistance;
    if (resolved.fadeStart == -2)
        resolved.fadeStart = DefaultRollOff.fadeStart;
    if (resolved.fadeEnd == -2)
        resolved.fadeEnd = DefaultRollOff.fadeEnd;
    return evaluate_rolloff(distance, &resolved);
}
