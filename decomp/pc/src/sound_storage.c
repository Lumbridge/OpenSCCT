/* Steam 252084 stores the texture list at a fixed address and the debug
   module handle in its own pointer slot. */
struct SoundTextureList;

struct SoundTextureList *SND_fn_pstGetSoundTextureList(void)
{
    return (struct SoundTextureList *)0x1132c730;
}

void *__stdcall SND_fn_vGetHModuleDbg(void)
{
    return *(void **)0x1132eab0;
}
