struct DwordVector { unsigned char allocator; unsigned *begin, *end, *limit; };
struct WordVector { unsigned char allocator; unsigned short *begin, *end, *limit; };
struct ByteRef { const unsigned char *ptr; };
struct Position { unsigned *ptr; };

struct DwordVector *__fastcall create_vector(struct DwordVector *v, struct ByteRef allocator)
{
    v->allocator = *allocator.ptr;
    v->begin = 0;
    v->end = 0;
    v->limit = 0;
    return v;
}

int __fastcall dword_count(const struct DwordVector *v)
{
    if (!v->begin) return 0;
    return v->end - v->begin;
}

int __fastcall word_capacity(const struct WordVector *v)
{
    if (!v->begin) return 0;
    return v->limit - v->begin;
}

extern void free_storage(void *p);
void __fastcall destroy_vector(struct DwordVector *v)
{
    free_storage(v->begin);
    v->begin = 0;
    v->end = 0;
    v->limit = 0;
}

unsigned *__fastcall erase_dwords(struct DwordVector *v, struct Position first, struct Position last)
{
    unsigned *out = first.ptr;
    unsigned *in = last.ptr;
    unsigned *end = v->end;
    while (in != end) *out++ = *in++;
    v->end = out;
    return first.ptr;
}

#define DEFINE_CONSTRUCT_RANGE(Type, Name) \
    Type *__stdcall construct_##Name(const Type *first, const Type *last, Type *out) \
    { \
        while (first != last) { \
            if (out) *out = *first; \
            ++first; \
            ++out; \
        } \
        return out; \
    }

DEFINE_CONSTRUCT_RANGE(unsigned short, words)
DEFINE_CONSTRUCT_RANGE(unsigned, dwords)

void __stdcall fill_words(unsigned short *out, unsigned count, const unsigned short *value)
{
    for (; count > 0; --count, ++out) {
        if (out) *out = *value;
    }
}
