struct EngineHeap;
struct HeapMethods {
    void *(__fastcall *allocate)(struct EngineHeap *heap, const void *scratch_edx,
                                 unsigned int bytes, const unsigned short *tag);
};
struct EngineHeap { const struct HeapMethods *methods; };
extern struct EngineHeap *engine_heap;
extern const unsigned short magma_tag[];

void *__cdecl allocate_engine_buffer(unsigned int bytes, const unsigned short *tag)
{
    if (!tag) tag = magma_tag;
    return engine_heap->methods->allocate(engine_heap, engine_heap->methods, bytes, tag);
}
