/* The one-word argument structs preserve the stack layout of MS x86 member
   calls. The object and allocator receivers are passed in ECX. */
struct ArrayBuffer { unsigned char *bytes; };
struct ArraySize { int value; };
struct ArrayTag { const unsigned short *text; };
struct Array {
    struct ArrayBuffer buffer;
    int count;
    int capacity;
};
struct ArrayAllocator;
struct ArrayAllocatorMethods {
    void *allocate;
    unsigned char *(__fastcall *resize)(struct ArrayAllocator *, struct ArrayBuffer, struct ArraySize, struct ArrayTag);
    void (__fastcall *release)(struct ArrayAllocator *, struct ArrayBuffer);
};
struct ArrayAllocator { const struct ArrayAllocatorMethods *methods; };
extern struct ArrayAllocator *array_allocator;
extern void *__cdecl array_move(void *to, const void *from, unsigned size);

static __forceinline void release_storage(struct Array *array)
{
    if (array->buffer.bytes) {
        struct ArrayBuffer storage;
        storage.bytes = array->buffer.bytes;
        array_allocator->methods->release(array_allocator, storage);
    }
    array->buffer.bytes = 0;
    array->capacity = 0;
    array->count = 0;
}

static __forceinline void remove_elements(struct Array *array, int index, int amount, int element_size,
                                         const unsigned short *allocation_tag)
{
    if (amount) {
        array_move(array->buffer.bytes + index * element_size,
                   array->buffer.bytes + (index + amount) * element_size,
                   (array->count - index - amount) * element_size);
        array->count -= amount;
        if ((3 * array->count < 2 * array->capacity ||
             (array->capacity - array->count) * element_size >= 16384) &&
            (array->capacity - array->count > 64 || array->count == 0)) {
            struct ArraySize bytes;
            struct ArrayTag tag;
            array->capacity = array->count;
            bytes.value = array->capacity * element_size;
            tag.text = allocation_tag;
            array->buffer.bytes = array_allocator->methods->resize(array_allocator, array->buffer, bytes, tag);
        }
    }
}

/* Each entry corresponds to a distinct, callable instantiation in this build. */
#define ARRAY_RELEASE(Entry) \
    void __fastcall array_release_##Entry(struct Array *array) { release_storage(array); }
#define ARRAY_REMOVE(Entry, Tag) \
    void __fastcall array_remove_##Entry(struct Array *array, struct ArraySize index, \
                                         struct ArraySize amount, struct ArraySize stride) \
    { remove_elements(array, index.value, amount.value, stride.value, Tag); }
