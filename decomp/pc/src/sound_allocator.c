/* Slot two receives the allocator in ECX and the allocation on the stack.
   Microsoft x86 fastcall passes this single-field aggregate on the stack. */
typedef struct AllocationPointer { void *value; } AllocationPointer;
typedef struct SoundAllocator SoundAllocator;
struct SoundAllocator {
    const struct {
        void *allocate;
        void *resize;
        void (__fastcall *release)(SoundAllocator *, AllocationPointer);
    } *methods;
};

#define ALLOCATOR (*(SoundAllocator **)0x111c88a0)

void __stdcall SND_fn_vFreeSnd(void *allocation)
{
    AllocationPointer block = { allocation };
    ALLOCATOR->methods->release(ALLOCATOR, block);
}
