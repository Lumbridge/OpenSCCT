typedef int SoundFixed;
extern void __stdcall SND_fn_vDisplayErrorEx(int code, const char *site, int detail);
extern SoundFixed __stdcall SND_fn_rDivRealRealSnd(SoundFixed num, SoundFixed den);
extern SoundFixed __stdcall SND_fn_rMulRealRealSnd(SoundFixed a, SoundFixed b);
extern const char attenuation_error_site[];
extern const SoundFixed distance_bands[4], slopes[4], intercepts[4];
extern const SoundFixed attenuation_table[4][128];
extern const SoundFixed distant_slope, distant_intercept;

#pragma code_seg(".text$B")
SoundFixed __cdecl distance_attenuation(SoundFixed distance, SoundFixed nearDistance)
{
    SoundFixed ratio;
    int band, index;
    if (nearDistance <= 0) {
        SND_fn_vDisplayErrorEx(30, attenuation_error_site, 3);
        return -96 * 65536;
    }
    ratio = SND_fn_rDivRealRealSnd(distance, nearDistance);
    if (ratio <= distance_bands[3]) {
        band = 0;
        while (ratio > distance_bands[band])
            ++band;
        index = (SND_fn_rMulRealRealSnd(slopes[band], ratio) + intercepts[band]) >> 16;
        return attenuation_table[band][index];
    }
    index = (SND_fn_rMulRealRealSnd(slopes[3], ratio) + intercepts[3]) >> 16;
    if (index <= 127)
        return attenuation_table[3][index];
    return SND_fn_rMulRealRealSnd(distant_slope, ratio) + distant_intercept;
}
