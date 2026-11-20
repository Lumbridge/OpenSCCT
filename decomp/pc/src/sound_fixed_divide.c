typedef long SoundReal;

static unsigned long magnitude(SoundReal value)
{
    return value < 0 ? 0ul - (unsigned long)value : (unsigned long)value;
}

SoundReal __stdcall SND_fn_rDivRealRealQuickSnd(SoundReal numerator, SoundReal denominator)
{
    if ((SoundReal)magnitude(numerator) < 0x8000)
        return (long)((unsigned long)numerator << 16) / denominator;
    if ((SoundReal)magnitude(numerator) < 0x800000)
        return (long)((unsigned long)numerator << 8) / (denominator >> 8);
    if ((SoundReal)magnitude(denominator) > 0x8000)
        return (unsigned long)(numerator / (denominator >> 8)) << 8;
    return (unsigned long)(numerator / denominator) << 16;
}
