/* The two test exports pass outputs for the CPUID and MMX capability flags
   to the same retail detector, whose address is bound by the manifest. */
void __cdecl sound_detect_cpu(unsigned long *pentium, unsigned long *mmx);

unsigned long __stdcall SND_fn_bTestSnd_Pentium(void)
{
    unsigned long pentium, mmx;
    sound_detect_cpu(&pentium, &mmx);
    return pentium;
}

int __stdcall SND_fn_bTestSnd_MMX(void)
{
    unsigned long pentium, mmx;
    sound_detect_cpu(&pentium, &mmx);
    return pentium && mmx;
}
