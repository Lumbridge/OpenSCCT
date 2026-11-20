struct Size { unsigned bytes; };
struct Tag { const unsigned short *text; };
struct Heap;

struct HeapMethods {
    void *(__fastcall *allocate)(struct Heap *, struct Size, struct Tag);
};

struct Heap { const struct HeapMethods *methods; };
extern struct Heap *engine_heap;
extern const unsigned short new_tag[];

void *__cdecl allocate_bytes(unsigned size)
{
    struct Size request;
    struct Tag tag;
    request.bytes = size;
    tag.text = new_tag;
    return engine_heap->methods->allocate(engine_heap, request, tag);
}
