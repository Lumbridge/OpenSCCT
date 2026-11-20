void __stdcall SND_fn_vFreeSnd(void *allocation);
struct SoundTimer;

void __stdcall SND_fn_vDestroyTimer(struct SoundTimer *timer)
{
    if (timer && timer != (struct SoundTimer *)-2)
        SND_fn_vFreeSnd(timer);
}
