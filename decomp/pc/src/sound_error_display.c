#define ERROR_FLAGS ((unsigned long *)0x1132b7b0)
const char *__cdecl sound_error_label(int code);
void __cdecl sound_report_error(const char *label, const char *context);
void __cdecl sound_end_error_display(void);

void __stdcall SND_fn_vDisplayError(int code, const char *context)
{
    sound_report_error(sound_error_label(code), context);
}

void __stdcall SND_fn_vDisplayErrorEx(int code, const char *context, unsigned long unused)
{
    if (ERROR_FLAGS[code]) {
        sound_report_error(sound_error_label(code), context);
        ERROR_FLAGS[code] = 1;
    }
}

void __stdcall SND_fn_vDesInitErrorSnd(void)
{
    sound_end_error_display();
}
