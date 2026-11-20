struct WideString {
    unsigned short *data;
    int count, capacity;
};
typedef struct { const struct WideString *value; } StringSource;
struct Allocator;
struct AllocatorMethods {
    void *allocate;
    void *(__fastcall *resize)(struct Allocator *, const struct AllocatorMethods *,
                               void *, unsigned int, const unsigned short *);
};
struct Allocator { const struct AllocatorMethods *methods; };
extern struct Allocator *engine_allocator;
extern const unsigned short word_array_tag[];
void *__cdecl memcpy(void *, const void *, unsigned int);
#pragma intrinsic(memcpy)

struct WideString *__fastcall copy_wide_string(struct WideString *destination, StringSource source)
{
    int count = source.value->count;
    destination->count = count;
    destination->capacity = count;
    destination->data = 0;
    destination->data = engine_allocator->methods->resize(
        engine_allocator, engine_allocator->methods, 0, count * sizeof(*destination->data), word_array_tag);
    if (destination->count)
        memcpy(destination->data, source.value->data, destination->count * sizeof(*destination->data));
    return destination;
}
