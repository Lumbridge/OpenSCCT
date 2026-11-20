void __cdecl sound_heap_detect_cpu(unsigned long *cpuid, unsigned long *mmx);
void __stdcall SND_fn_vDisplayError(int code, const char *context);

static int missing_cpu_feature(void)
{
    SND_fn_vDisplayError(5, (const char *)0x111715ac);
    return 0;
}

int __stdcall SND_fn_bInitMallocSnd(void)
{
    unsigned long cpuid, mmx;
    sound_heap_detect_cpu(&cpuid, &mmx);
    if (!cpuid)
        return missing_cpu_feature();
    if (!mmx)
        return missing_cpu_feature();
    return 1;
}
