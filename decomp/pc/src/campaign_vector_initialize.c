struct Size { unsigned value; };
struct Vector { unsigned reserved; unsigned char *first, *end, *limit; };
extern unsigned allocation_kind;
extern void *__cdecl allocate_buffer(unsigned, unsigned);

#define INITIALIZE_VECTOR(Stride) \
    struct Element##Stride { unsigned char bytes[Stride]; }; \
    unsigned char __fastcall initialize_##Stride(struct Vector *v, struct Size count) \
    { \
        v->first = 0; \
        v->end = 0; \
        v->limit = 0; \
        if (!count.value) return 0; \
        if (count.value <= ~0u / Stride) { \
            unsigned bytes = count.value * Stride; \
            unsigned char *first = allocate_buffer(bytes, allocation_kind); \
            v->limit = (unsigned char *)((struct Element##Stride *)first + count.value); \
            v->first = first; \
            v->end = first; \
        } \
        return 1; \
    }

INITIALIZE_VECTOR(4)
INITIALIZE_VECTOR(8)
INITIALIZE_VECTOR(16)
INITIALIZE_VECTOR(32)
