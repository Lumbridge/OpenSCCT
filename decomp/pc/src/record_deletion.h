struct Record { const void *methods; };
struct DeleteFlags { unsigned value; };
struct Allocation { struct Record *object; };
struct Heap;
struct HeapOps {
    void *reserved[2];
    void (__fastcall *release)(struct Heap *, struct Allocation);
};
struct Heap { const struct HeapOps *ops; };
extern struct Heap *engine_heap;

static __forceinline void free_on_heap(struct Record *object)
{
    struct Allocation allocation;
    allocation.object = object;
    engine_heap->ops->release(engine_heap, allocation);
}

#define DELETE_RECORD(Entry, Destroy, Free) \
    struct Record *__fastcall Entry(struct Record *self, struct DeleteFlags flags) \
    { \
        Destroy(self); \
        if (flags.value & 1) Free(self); \
        return self; \
    }

#define DELETE_WITH_TABLE(Entry, Methods, Destroy, Free) \
    struct Record *__fastcall Entry(struct Record *self, struct DeleteFlags flags) \
    { \
        self->methods = Methods; \
        Destroy(self); \
        if (flags.value & 1) Free(self); \
        return self; \
    }
