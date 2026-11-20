typedef int (__stdcall *ReadCounter)(long long *);
#define QUERY_COUNTER (*(ReadCounter const *)0x110b72e4)
#define QUERY_FREQUENCY (*(ReadCounter const *)0x110b72e0)
#define ZERO_SECONDS (*(const float *)0x110b8220)

typedef struct SoundTimer {
    long paused;
    long alignment;
    long long origin;
    long long pause_start;
} SoundTimer;

float __stdcall SND_fn_fGetCurrentTime(const SoundTimer *timer)
{
    long long now, frequency;
    long long elapsed;
    if (!timer || timer == (const SoundTimer *)-2 || !QUERY_FREQUENCY(&frequency))
        return ZERO_SECONDS;

    if (timer->paused) {
        elapsed = timer->pause_start - timer->origin;
    } else {
        if (!QUERY_COUNTER(&now))
            return ZERO_SECONDS;
        elapsed = now - timer->origin;
    }
    return (float)elapsed / (float)frequency;
}
