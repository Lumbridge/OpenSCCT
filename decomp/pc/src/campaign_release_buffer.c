struct buffer_argument { void *value; };
struct engine_allocator;
struct allocator_methods {
    void *reserved[2];
    void (__fastcall *release)(struct engine_allocator *, struct buffer_argument);
};
struct engine_allocator { const struct allocator_methods *methods; };
extern struct engine_allocator *array_allocator;

void __cdecl release_engine_buffer(void *pointer)
{
    struct buffer_argument storage;
    storage.value = pointer;
    array_allocator->methods->release(array_allocator, storage);
}
