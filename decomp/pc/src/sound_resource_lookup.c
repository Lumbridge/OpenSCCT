void __stdcall SND_fn_vDisplayError(int code, const char *context);
#define SOUND_READY (*(const long *)0x1132bb4c)
#define LOOKUP_MODE (*(const long *)0x111e43d0)
void *__stdcall sound_bin_res_mode0(unsigned long id);
void *__stdcall sound_bin_res_mode1(unsigned long id);
void *__cdecl sound_bin_event_mode0(unsigned long id);
void *__cdecl sound_bin_event_mode1(unsigned long id);
void *__cdecl sound_handle_by_editor_id_mode0(unsigned long id);
void *__cdecl sound_handle_by_editor_id_mode1(unsigned long id);
void *__stdcall sound_resource_by_editor_id_mode0(unsigned long id);
void *__stdcall sound_resource_by_editor_id_mode1(unsigned long id);

void *__stdcall SND_fn_pGetBinRes(unsigned long id)
{
    if (!SOUND_READY) {
        SND_fn_vDisplayError(0, (const char *)0x11172d68);
        return 0;
    }
    switch (LOOKUP_MODE) {
    case 0: return sound_bin_res_mode0(id);
    case 1: return sound_bin_res_mode1(id);
    default:
        SND_fn_vDisplayError(0, (const char *)0x11172d28);
        return 0;
    }
}

void *__stdcall SND_fn_pGetBinEvent(unsigned long id)
{
    if (!SOUND_READY) {
        SND_fn_vDisplayError(0, (const char *)0x11172df0);
        return 0;
    }
    switch (LOOKUP_MODE) {
    case 0: return sound_bin_event_mode0(id);
    case 1: return sound_bin_event_mode1(id);
    default:
        SND_fn_vDisplayError(0, (const char *)0x11172db4);
        return 0;
    }
}

void *__stdcall SND_fn_hGetSoundEventHandleFromEditorId(unsigned long id)
{
    if (!SOUND_READY) {
        SND_fn_vDisplayError(0, (const char *)0x11173138);
        return 0;
    }
    switch (LOOKUP_MODE) {
    case 0: return sound_handle_by_editor_id_mode0(id);
    case 1: return sound_handle_by_editor_id_mode1(id);
    default:
        SND_fn_vDisplayError(0, (const char *)0x111730e8);
        return 0;
    }
}

void *__stdcall SND_fn_pstGetResFromEdIdSnd(unsigned long id)
{
    switch (LOOKUP_MODE) {
    case 0: return sound_resource_by_editor_id_mode0(id);
    case 1: return sound_resource_by_editor_id_mode1(id);
    default:
        SND_fn_vDisplayError(0, (const char *)0x111730e8);
        return 0;
    }
}
