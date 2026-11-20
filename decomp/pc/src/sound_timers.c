/* Decoded import slot in the Steam 252084 campaign image. */
typedef int (__stdcall *ReadCounter)(long long *);
#define QUERY_PERFORMANCE_COUNTER (*(ReadCounter const *)0x110b72e4)

typedef struct SoundTimer {
    long paused;
    long alignment;
    long long origin;
    long long pause_start;
} SoundTimer;

void __stdcall SND_fn_vResetTimer(SoundTimer *timer)
{
    if (timer && timer != (SoundTimer *)-2 &&
        QUERY_PERFORMANCE_COUNTER(&timer->origin))
        timer->paused = 0;
}

void __stdcall SND_fn_vPauseTimer(SoundTimer *timer)
{
    if (timer && timer != (SoundTimer *)-2 && !timer->paused &&
        QUERY_PERFORMANCE_COUNTER(&timer->pause_start))
        timer->paused = 1;
}
