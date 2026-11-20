struct Count { unsigned value; };
extern unsigned allocation_kind;
extern void *__cdecl allocate_buffer(unsigned bytes, unsigned kind);

#define ELEMENT_ALLOCATOR(Stride) \
    void *__fastcall allocate_##Stride(void *allocator, struct Count count) \
    { \
        return allocate_buffer(count.value * Stride, allocation_kind); \
    }

ELEMENT_ALLOCATOR(1)
ELEMENT_ALLOCATOR(2)
ELEMENT_ALLOCATOR(4)
ELEMENT_ALLOCATOR(8)
ELEMENT_ALLOCATOR(12)
ELEMENT_ALLOCATOR(16)
ELEMENT_ALLOCATOR(32)
ELEMENT_ALLOCATOR(48)
ELEMENT_ALLOCATOR(56)
ELEMENT_ALLOCATOR(60)
