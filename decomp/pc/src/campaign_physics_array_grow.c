struct Array { void *data; int count; unsigned capacity; };
struct Manager;
struct Methods {
    void *reserved[4];
    void *(__fastcall *allocate)(struct Manager *, unsigned, unsigned, unsigned);
    void (__fastcall *release)(struct Manager *, const struct Methods *, void *, unsigned, unsigned);
};
struct Manager { const struct Methods *methods; };
extern struct Manager *manager;
struct ByteCount { unsigned value; };
extern void __cdecl copy_storage(void *, const void *, struct ByteCount);
void __cdecl grow(struct Array *array, int capacity, unsigned width)
{
    unsigned bytes = capacity * width;
    void *buffer = manager->methods->allocate(manager, bytes, bytes, 18);
    {
        void *dst;
        const void *src;
        struct ByteCount size;
        dst = buffer;
        src = array->data;
        size.value = array->count * width;
        copy_storage(dst, src, size);
    }
    if (!(array->capacity & 0x80000000u))
        manager->methods->release(manager, manager->methods, array->data, (array->capacity & 0x7fffffffu) * width, 18);
    array->data = buffer;
    array->capacity = capacity;
}
