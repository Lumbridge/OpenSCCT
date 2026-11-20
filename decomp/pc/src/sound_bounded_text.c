/* Bounded audio text operations. Capacities include the terminator. */
typedef unsigned int SoundSize;
extern SoundSize __cdecl strlen(const char *text);
extern char *__cdecl strncpy(char *dest, const char *src, SoundSize capacity);
extern char *__cdecl strncat(char *dest, const char *src, SoundSize count);
extern const char sound_text_overflow_message[];
extern void __stdcall SND_fn_vDisplayErrorEx(unsigned code, const char *message, unsigned category);

void __stdcall SND_fn_vStrncpy(char *dest, const char *src, SoundSize capacity)
{
    int truncated = 0;
    if (capacity > 0) {
        if (strlen(src) + 1 > capacity) {
            truncated = 1;
            SND_fn_vDisplayErrorEx(8, sound_text_overflow_message, 3);
        }
        strncpy(dest, src, capacity);
        if (truncated)
            dest[capacity - 1] = 0;
    }
}

void __stdcall SND_fn_vStrncat(char *dest, const char *src, SoundSize capacity)
{
    if (capacity > 0 && capacity > strlen(dest) + 1) {
        if (strlen(dest) + strlen(src) + 1 > capacity)
            SND_fn_vDisplayErrorEx(8, sound_text_overflow_message, 3);
        strncat(dest, src, capacity - strlen(dest) - 1);
    }
}
