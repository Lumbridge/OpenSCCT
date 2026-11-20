/* Steam 252084 exports these catalogue queries. None reads a parameter or
   writes an output. The unused parameter types describe x86 stack slots. */
unsigned long __stdcall SND_fn_ulGetNumberOfSoundEventGroup(void)
{
    return 0;
}

unsigned long __stdcall SND_fn_ulGetListOfSoundEventGroupName(unsigned long arg1, unsigned long arg2)
{
    return 0;
}

unsigned long __stdcall SND_fn_ulGetNumberOfSoundEventInGroup(unsigned long arg1)
{
    return 0;
}

unsigned long __stdcall SND_fn_ulGetListOfSoundEventNameInGroup(unsigned long arg1, unsigned long arg2, unsigned long arg3)
{
    return 0;
}

int __stdcall SND_fn_bGetSoundEventNameFromEditorId(unsigned long arg1, unsigned long arg2, unsigned long arg3)
{
    return 0;
}
