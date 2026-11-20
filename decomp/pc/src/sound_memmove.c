void *__cdecl sound_move_bytes(void *destination, const void *source, unsigned long count);

void __stdcall SND_fn_vMemMoveSnd(void *destination, const void *source, unsigned long count)
{
    sound_move_bytes(destination, source, count);
}
