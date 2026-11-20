typedef struct Heap Heap;
struct Heap {
    const struct {
        void *(__fastcall *allocate)(Heap *, unsigned long unused_edx, unsigned long size, const void *tag);
        void *(__fastcall *resize)(Heap *, void *unused_edx, void *allocation, unsigned long size, const void *tag);
        void *release;
    } *methods;
};
extern Heap *engine_heap;
extern const unsigned short gameservice_allocate_tag[], gameservice_resize_tag[];
void *__stdcall gameservice_allocate(unsigned long size)
{
    return engine_heap->methods->allocate(engine_heap, size, size, gameservice_allocate_tag);
}
void *__stdcall gameservice_resize(void *allocation, unsigned long size)
{
    return engine_heap->methods->resize(engine_heap, allocation, allocation, size, gameservice_resize_tag);
}
