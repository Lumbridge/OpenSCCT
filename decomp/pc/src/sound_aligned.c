void __stdcall SND_fn_vFreeSnd(void *allocation);
void __stdcall SND_fn_vMemCopySnd(void *destination, const void *source, unsigned long count);

typedef struct AlignedHeader {
    unsigned long size;
    void *allocation;
} AlignedHeader;

void *__stdcall SND_fn_pvMallocSndAligned(unsigned long size, unsigned long alignment);

void __stdcall SND_fn_vFreeSndAligned(void *aligned, unsigned long alignment)
{
    SND_fn_vFreeSnd(((AlignedHeader *)aligned)[-1].allocation);
}

void *__stdcall SND_fn_pvReAllocSndAligned(void *aligned, unsigned long size, unsigned long alignment)
{
    void *replacement;
    if (!size) {
        if (aligned)
            SND_fn_vFreeSndAligned(aligned, alignment);
        return 0;
    }
    replacement = SND_fn_pvMallocSndAligned(size, alignment);
    if (replacement && aligned) {
        unsigned long old_size = ((AlignedHeader *)aligned)[-1].size;
        SND_fn_vMemCopySnd(replacement, aligned, old_size <= size ? old_size : size);
    }
    if (aligned)
        SND_fn_vFreeSndAligned(aligned, alignment);
    return replacement;
}
