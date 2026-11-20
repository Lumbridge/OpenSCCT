typedef int SoundFixed;
/* Decoded from the retail index and its mirrored lookup. */
extern const SoundFixed PanAttenuation[129];
void __stdcall SND_fn_vVolPanToVolLR(SoundFixed volume, unsigned char pan, SoundFixed *left, SoundFixed *right)
{
    const SoundFixed minimum = -96 * 65536;
    if (left && right) {
        *right = volume + PanAttenuation[pan];
        *left = volume + PanAttenuation[128 - pan];
        if (*right < minimum)
            *right = minimum;
        if (*left < minimum)
            *left = minimum;
    }
}
