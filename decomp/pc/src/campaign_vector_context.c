struct Size { unsigned value; };
struct Element4 { unsigned char bytes[4]; };
struct Vector {
    unsigned reserved;
    struct Element4 *first, *end, *limit;
};

__declspec(noreturn) void __fastcall vector_length_error(struct Vector *);
extern const unsigned short allocation_source[];
extern void *__cdecl allocation_context(void);
extern void *__cdecl allocate_buffer(void *context, unsigned bytes,
                                     const unsigned short *source, unsigned line, unsigned kind);

unsigned char __fastcall initialize_4(struct Vector *v, struct Size count)
{
    struct Element4 *first;

    v->first = 0;
    v->end = 0;
    v->limit = 0;
    if (!count.value)
        return 0;
    if (count.value > ~0u / sizeof(*first))
        vector_length_error(v);

    first = allocate_buffer(allocation_context(), count.value * sizeof(*first),
                            allocation_source, 0, 7);
    v->limit = first + count.value;
    v->first = first;
    v->end = first;
    return 1;
}
