/* A one-word aggregate models each incoming x86 member-function stack argument. */
struct BufferArg { void *value; };
union ArrayData {
    unsigned char *bytes;
    struct BufferArg block;
};
struct Array {
    union ArrayData data;
    int count;
    int capacity;
};
struct SimpleArray {
    union ArrayData data;
    int count;
};
struct Allocator;
struct SizeArg { int value; };
struct TagArg { const unsigned short *value; };
struct AllocatorOps {
    void *(__fastcall *allocate)(struct Allocator *, struct SizeArg, struct TagArg);
    void *(__fastcall *reallocate)(struct Allocator *, struct BufferArg, struct SizeArg, struct TagArg);
    void (__fastcall *release)(struct Allocator *, struct BufferArg);
};
struct Allocator { const struct AllocatorOps *ops; };
extern struct Allocator *engine_allocator;
extern const unsigned short array_tag[], add_tag[], simple_tag[];
__declspec(dllimport) void *__cdecl memmove(void *dest, const void *src, unsigned size);

static __forceinline void *allocate(int size, const unsigned short *tag)
{
    struct SizeArg bytes;
    struct TagArg label;
    bytes.value = size;
    label.value = tag;
    return engine_allocator->ops->allocate(engine_allocator, bytes, label);
}

static __forceinline void *reallocate(const struct BufferArg *data, int size, const unsigned short *tag)
{
    struct SizeArg bytes;
    struct TagArg label;
    bytes.value = size;
    label.value = tag;
    return engine_allocator->ops->reallocate(engine_allocator, *data, bytes, label);
}

static __forceinline void release(const struct BufferArg *data)
{
    engine_allocator->ops->release(engine_allocator, *data);
}

struct Array *__fastcall init_array(struct Array *array)
{
    array->data.bytes = 0;
    array->count = 0;
    array->capacity = 0;
    return array;
}

void __fastcall release_array(struct Array *array)
{
    if (array->data.bytes) {
        struct BufferArg old;
        old.value = array->data.bytes;
        release(&old);
        array->data.bytes = 0;
    }
    array->capacity = 0;
    array->count = 0;
}

int __fastcall add_array(struct Array *array, struct SizeArg amount, struct SizeArg element)
{
    int index = array->count;
    array->count += amount.value;
    if (array->count > array->capacity) {
        int bytes;
        array->capacity = array->count + (3 * array->count) / 8 + 32;
        bytes = array->capacity * element.value;
        if (!array->data.bytes)
            array->data.bytes = allocate(bytes, add_tag);
        else {
            struct BufferArg old;
            old.value = array->data.bytes;
            array->data.bytes = reallocate(&old, bytes, array_tag);
        }
    }
    return index;
}

void __fastcall remove_array(struct Array *array, struct SizeArg index, struct SizeArg amount, struct SizeArg element)
{
    if (amount.value) {
        memmove(array->data.bytes + index.value * element.value,
                array->data.bytes + (index.value + amount.value) * element.value,
                (array->count - index.value - amount.value) * element.value);
        array->count -= amount.value;
        if ((3 * array->count < 2 * array->capacity ||
             (array->capacity - array->count) * element.value >= 16384) &&
            (array->capacity - array->count > 64 || array->count == 0)) {
            array->capacity = array->count;
            if (!array->count) {
                release(&array->data.block);
                array->data.bytes = 0;
            } else
                array->data.bytes = reallocate(&array->data.block, array->capacity * element.value, array_tag);
        }
    }
}

void __fastcall remove_simple_array(struct SimpleArray *array, struct SizeArg index, struct SizeArg amount, struct SizeArg element)
{
    if (amount.value) {
        memmove(array->data.bytes + index.value * element.value,
                array->data.bytes + (index.value + amount.value) * element.value,
                (array->count - index.value - amount.value) * element.value);
        array->count -= amount.value;
        if (!array->count) {
            release(&array->data.block);
            array->data.bytes = 0;
        } else
            array->data.bytes = reallocate(&array->data.block, array->count * element.value, simple_tag);
    }
}
