int __cdecl sound_probe_cpu_sse(void);
unsigned char __cdecl sound_probe_sse_state(void);
int __cdecl sound_probe_sse_arithmetic(void);

int __stdcall SND_Is_SSE_Supported(void)
{
    if (!sound_probe_cpu_sse())
        return 0;
    else if (!sound_probe_sse_state())
        return 0;
    else
        return sound_probe_sse_arithmetic() ? 1 : 0;
}
