/* Slot two receives the allocator in ECX and the allocation on the stack.
   Microsoft x86 fastcall passes this single-field aggregate on the stack. */
typedef struct AllocationPointer { void *value; } AllocationPointer;
typedef struct Heap Heap;
struct Heap {
    const struct {
        void *allocate;
        void *resize;
        void (__fastcall *release)(Heap *, AllocationPointer);
    } *methods;
};

extern Heap *engine_heap;

void __stdcall release_171510(void *allocation)
{
    AllocationPointer block = { allocation };
    engine_heap->methods->release(engine_heap, block);
}
