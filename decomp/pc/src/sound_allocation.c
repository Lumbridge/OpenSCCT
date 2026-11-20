typedef struct SoundAllocator SoundAllocator;
struct SoundAllocator {
    const struct {
        void *(__fastcall *allocate)(SoundAllocator *, unsigned long unused_edx, unsigned long size, const void *tag);
        void *(__fastcall *resize)(SoundAllocator *, void *unused_edx, void *allocation, unsigned long size, const void *tag);
        void *release;
    } *methods;
};
#define ALLOCATOR (*(SoundAllocator **)0x111c88a0)
#define SOUND_ALLOCATION_TAG ((const void *)0x111e42c8)
void *__stdcall SND_fn_pvMallocSnd(unsigned long size)
{
    return ALLOCATOR->methods->allocate(ALLOCATOR, size, size, SOUND_ALLOCATION_TAG);
}
void *__stdcall SND_fn_pvReAllocSnd(void *allocation, unsigned long size)
{
    return ALLOCATOR->methods->resize(ALLOCATOR, allocation, allocation, size, SOUND_ALLOCATION_TAG);
}
