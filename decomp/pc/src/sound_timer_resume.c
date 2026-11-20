/* Decoded import slot in the Steam 252084 campaign image. */
typedef int (__stdcall *ReadCounter)(long long *);
#define QUERY_PERFORMANCE_COUNTER (*(ReadCounter const *)0x110b72e4)

typedef struct SoundTimer {
    long paused;
    long alignment;
    long long origin;
    long long pause_start;
} SoundTimer;

void __stdcall SND_fn_vResumeTimer(SoundTimer *timer)
{
    long long now;
    if (timer && timer != (SoundTimer *)-2 && timer->paused) {
        timer->paused = 0;
        if (QUERY_PERFORMANCE_COUNTER(&now))
            timer->origin += now - timer->pause_start;
    }
}
