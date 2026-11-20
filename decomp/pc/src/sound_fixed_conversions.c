typedef long SoundReal;
SoundReal __stdcall SND_fn_rAbsRealSnd(SoundReal value)
{
    return value < 0 ? (SoundReal)(0ul - (unsigned long)value) : value;
}
void __stdcall SND_fn_vSndRealTwinToSndReal(long twin, SoundReal *first, SoundReal *second)
{
    if (first)
        *first = (twin >> 8) & ~255L;
    if (second)
        *second = (long)((unsigned long)twin << 16) >> 8;
}
long __stdcall SND_fn_rtSndRealToSndRealTwin(SoundReal first, SoundReal second)
{
    return (((unsigned long)first << 8) & 0xffff0000ul) | ((unsigned long)(second >> 8) & 0xffff);
}
