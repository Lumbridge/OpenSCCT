/* The named campaign exports encode the division in signed 16.16 arithmetic.
   Link the recovered caller pair at its retail addresses for exact REL32s. */
#pragma code_seg(".text$B")
typedef int SoundFixed;

extern SoundFixed __stdcall SND_fn_rDivRealRealSnd(SoundFixed numerator, SoundFixed denominator);
extern int DopplerPropagationSpeed;

void __stdcall SND_fn_vSetDopplerFactor(SoundFixed factor)
{
    if (factor)
        DopplerPropagationSpeed = SND_fn_rDivRealRealSnd(300 * 65536, factor) >> 16;
    else
        DopplerPropagationSpeed = 0;
}

#pragma code_seg(".text$C")
SoundFixed __stdcall SND_fn_rGetDopplerFactor(void)
{
    if (DopplerPropagationSpeed)
        return SND_fn_rDivRealRealSnd(300 * 65536, (unsigned)DopplerPropagationSpeed << 16);
    return 0;
}
