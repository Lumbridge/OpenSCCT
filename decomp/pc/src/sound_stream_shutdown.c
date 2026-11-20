void __cdecl sound_shutdown_async_io(void);

void __stdcall SND_fn_vDesInitStreamAsyncSnd(void)
{
    sound_shutdown_async_io();
}
