#include "campaign_array_storage.h"

struct ResizeHeap;
struct ResizeMethods {
    void *allocate;
    unsigned char *(__fastcall *resize)(struct ResizeHeap *, const void *, void *, int, struct ArrayTag);
};
struct ResizeHeap { const struct ResizeMethods *methods; };
extern struct ResizeHeap *resize_heap;

static __forceinline struct Array *initialize_array(struct Array *array, int width, int capacity, const unsigned short *label)
{
    struct ArraySize bytes;
    struct ArrayTag tag;

    array->count = capacity;
    array->capacity = capacity;
    array->buffer.bytes = 0;
    bytes.value = capacity * width;
    tag.text = label;
    array->buffer.bytes = resize_heap->methods->resize(resize_heap, resize_heap->methods, 0, bytes.value, tag);
    return array;
}

#define INITIALIZE_DYNAMIC(Entry, Tag) \
    struct Array *__fastcall Entry(struct Array *array, struct ArraySize count, struct ArraySize size) \
    { \
        return initialize_array(array, size.value, count.value, Tag); \
    }
#define INITIALIZE_FIXED(Entry, Width, Tag) \
    struct Array *__fastcall Entry(struct Array *array, struct ArraySize capacity) \
    { \
        return initialize_array(array, Width, capacity.value, Tag); \
    }

extern const unsigned short tag_7b78d8[];
extern const unsigned short tag_7b7b08[];
extern const unsigned short tag_7b8f84[];
extern const unsigned short tag_7ba13c[];
extern const unsigned short tag_7ba17c[];
extern const unsigned short tag_7bc158[];
extern const unsigned short tag_7bc180[];
extern const unsigned short tag_7be9a8[];
extern const unsigned short tag_7cddbc[];

INITIALIZE_DYNAMIC(initialize_0075c0, tag_7b78d8)
INITIALIZE_DYNAMIC(initialize_04bd20, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_0662a0, tag_7be9a8)
INITIALIZE_DYNAMIC(initialize_0715a0, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_0774e0, tag_7b7b08)
INITIALIZE_FIXED(initialize_077f60, 16, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_07f290, tag_7bc180)
INITIALIZE_FIXED(initialize_080130, 12, tag_7bc180)
INITIALIZE_DYNAMIC(initialize_080840, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_081570, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_081640, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_081710, tag_7b7b08)
INITIALIZE_FIXED(initialize_081ef0, 4, tag_7b7b08)
INITIALIZE_FIXED(initialize_082790, 128, tag_7b7b08)
INITIALIZE_FIXED(initialize_0827d0, 8, tag_7b7b08)
INITIALIZE_FIXED(initialize_082830, 124, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_09df50, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_0a9590, tag_7b7b08)
INITIALIZE_FIXED(initialize_0a95d0, 4, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_0b7d20, tag_7cddbc)
INITIALIZE_FIXED(initialize_0be910, 4, tag_7cddbc)
INITIALIZE_DYNAMIC(initialize_0bea40, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_0beb20, tag_7b7b08)
INITIALIZE_FIXED(initialize_0bfb00, 132, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_0dda70, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_10b920, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_129300, tag_7b7b08)
INITIALIZE_FIXED(initialize_129840, 16, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_131740, tag_7b7b08)
INITIALIZE_FIXED(initialize_132200, 4, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_15a2e0, tag_7bc158)
INITIALIZE_DYNAMIC(initialize_15b680, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_1ad870, tag_7ba13c)
INITIALIZE_DYNAMIC(initialize_1ad910, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_1c68d0, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_1e9d40, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_1e9da0, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_1e9e00, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_22d4d0, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_22d510, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_22d550, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_2db000, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_2e5df0, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_2f4010, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_31d290, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_34cf90, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_39b4d0, tag_7b7b08)
INITIALIZE_FIXED(initialize_3a2cf0, 4, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_3cdeb0, tag_7b8f84)
INITIALIZE_DYNAMIC(initialize_3d73c0, tag_7ba17c)
INITIALIZE_DYNAMIC(initialize_3da090, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_3da4e0, tag_7b7b08)
INITIALIZE_DYNAMIC(initialize_411dc0, tag_7b7b08)
