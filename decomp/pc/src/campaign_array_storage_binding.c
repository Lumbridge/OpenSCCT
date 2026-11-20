struct Array { void *data; unsigned size, capacity_flags; };
struct InlineArray { struct Array array; unsigned char storage[1]; };

struct Array *__fastcall use_buffer(struct Array *array, unsigned unused_edx,
                                    void *data, unsigned size, unsigned capacity)
{
    array->data = data;
    array->size = size;
    array->capacity_flags = capacity | 0x80000000u;
    return array;
}

#define INLINE_ARRAY(Capacity) \
    struct InlineArray *__fastcall use_inline_##Capacity(struct InlineArray *owner, \
                                                         unsigned unused_edx, unsigned size) \
    { \
        owner->array.data = owner->storage; \
        owner->array.size = size; \
        owner->array.capacity_flags = 0x80000000u | Capacity; \
        return owner; \
    }

INLINE_ARRAY(1)
INLINE_ARRAY(3)
INLINE_ARRAY(4)
INLINE_ARRAY(8)
INLINE_ARRAY(10)
INLINE_ARRAY(16)
INLINE_ARRAY(64)
INLINE_ARRAY(100)
INLINE_ARRAY(128)
INLINE_ARRAY(256)
INLINE_ARRAY(1024)
