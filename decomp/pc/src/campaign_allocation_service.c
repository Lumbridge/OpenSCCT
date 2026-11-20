struct Context;
struct Methods {
    void *reserved;
    void (__fastcall *enter)(struct Context *);
    void (__fastcall *leave)(struct Context *);
};
struct Context { const struct Methods *methods; };
struct Label { const unsigned short *ptr; };
typedef void (__cdecl *Release)(void *);
extern void *(__cdecl *allocate_callback)(unsigned);
extern Release release_callback;
extern unsigned header_size;
extern void *__cdecl default_allocate(unsigned);
extern void __cdecl default_release(void *);
extern struct Context *shared_context;
extern unsigned char initializing;
extern const unsigned short source_file[], context_name[];
extern void *__cdecl allocate_context(unsigned, const unsigned short *, unsigned);
extern struct Context *__fastcall construct_context(void *, struct Label);

struct Context *__cdecl get_context(void)
{
    if (!shared_context) {
        void *memory;
        struct Label label;
        if (initializing)
            return 0;
        initializing = 1;
        memory = allocate_context(60, source_file, 267);
        label.ptr = context_name;
        shared_context = memory ? construct_context(memory, label) : 0;
    }
    return shared_context;
}

static __forceinline unsigned offset(void)
{
    if (!header_size)
        header_size = 4;
    return header_size;
}

void *__cdecl allocate_block(struct Context *context, unsigned bytes, const unsigned short *label,
                             unsigned options, unsigned category)
{
    unsigned size = bytes + ((0u - bytes) & 3u) + 8;
    unsigned char *base = allocate_callback ? allocate_callback(size) : default_allocate(size);
    unsigned char *block;
    if (context)
        context->methods->enter(context);
    block = base + offset();
    *(Release *)base = release_callback;
    if (context)
        context->methods->leave(context);
    return block;
}

void __cdecl release_block(struct Context *context, unsigned char *block, unsigned category)
{
    if (block) {
        unsigned char *base;
        Release release;
        if (context)
            context->methods->enter(context);
        base = block - offset();
        release = *(Release *)base;
        if (release)
            release(base);
        else
            default_release(base);
        if (context)
            context->methods->leave(context);
    }
}
