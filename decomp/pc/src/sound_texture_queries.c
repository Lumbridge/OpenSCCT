/* The export names and stack widths are known. Parameter names and types
   model the x86 ABI; these catalogue entries never read their arguments. */
long __stdcall SND_fn_lGetSoundTextureIdArray(void *ids, unsigned long capacity)
{
    return 0;
}

long __stdcall SND_fn_lGetSoundTextureId(const void *key)
{
    return -2;
}

long __stdcall SND_fn_lGetSoundTextureName(long id, void *name, unsigned long capacity)
{
    return 0;
}
