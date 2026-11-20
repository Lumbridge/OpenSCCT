typedef unsigned long Word;
int __cdecl sound_init_async_workers(Word count);
int __cdecl sound_format_message(char *out, const char *format, ...);
void __stdcall SND_fn_vDisplayError(int code, const char *context);
extern Word stream_count;
extern float stream_budget;

int __stdcall SND_fn_bInitStreamAsyncSnd(Word count, float limit)
{
    char message[256];
    float total;
    stream_count = count;
    total = count * stream_budget;
    if (total > limit) {
        sound_format_message(message, (const char *)0x11178250, total, limit);
        SND_fn_vDisplayError(8, message);
    }
    return sound_init_async_workers(count);
}
