#include "campaign_array_resize_abi.h"

extern const unsigned short tag_7b78d8[];
extern const unsigned short tag_7b7b08[];
extern const unsigned short tag_7b9b70[];
extern const unsigned short tag_7ba17c[];
extern const unsigned short tag_7bc158[];
extern const unsigned short tag_7db4cc[];

#define RESET_ARRAY(Entry, Width, Tag) \
    void __fastcall Entry(struct Array *array, struct ArraySize capacity) \
    { \
        struct ArraySize bytes; \
        struct ArrayTag tag; \
        array->capacity = capacity.value; \
        array->count = 0; \
        bytes.value = capacity.value * (Width); \
        tag.text = Tag; \
        array->buffer.bytes = resize_heap->methods->resize(resize_heap, resize_heap->methods, array->buffer, bytes.value, tag); \
    }

RESET_ARRAY(reset_0095e0, 2, tag_7b78d8)
RESET_ARRAY(reset_0155b0, 8, tag_7b9b70)
RESET_ARRAY(reset_015620, 4, tag_7b7b08)
RESET_ARRAY(reset_017720, 4, tag_7ba17c)
RESET_ARRAY(reset_018cd0, 4, tag_7b7b08)
RESET_ARRAY(reset_01f7a0, 4, tag_7b7b08)
RESET_ARRAY(reset_035560, 4, tag_7bc158)
RESET_ARRAY(reset_0673e0, 4, tag_7b7b08)
RESET_ARRAY(reset_073c00, 16, tag_7b7b08)
RESET_ARRAY(reset_07d750, 16, tag_7b7b08)
RESET_ARRAY(reset_07d960, 8, tag_7b7b08)
RESET_ARRAY(reset_081350, 28, tag_7b7b08)
RESET_ARRAY(reset_082030, 4, tag_7b7b08)
RESET_ARRAY(reset_0820b0, 192, tag_7b7b08)
RESET_ARRAY(reset_082220, 28, tag_7b7b08)
RESET_ARRAY(reset_0822a0, 20, tag_7b7b08)
RESET_ARRAY(reset_082420, 8, tag_7b7b08)
RESET_ARRAY(reset_092120, 72, tag_7b7b08)
RESET_ARRAY(reset_0ad340, 4, tag_7b7b08)
RESET_ARRAY(reset_0bf7c0, 28, tag_7b7b08)
RESET_ARRAY(reset_0bfda0, 28, tag_7b7b08)
RESET_ARRAY(reset_0c00e0, 132, tag_7b7b08)
RESET_ARRAY(reset_0dd690, 48, tag_7b7b08)
RESET_ARRAY(reset_0df700, 36, tag_7b7b08)
RESET_ARRAY(reset_0df7b0, 16, tag_7b7b08)
RESET_ARRAY(reset_0df8c0, 8, tag_7b7b08)
RESET_ARRAY(reset_0df940, 16, tag_7b7b08)
RESET_ARRAY(reset_0e0050, 8, tag_7b7b08)
RESET_ARRAY(reset_0e26c0, 156, tag_7b7b08)
RESET_ARRAY(reset_0e2760, 16, tag_7b7b08)
RESET_ARRAY(reset_0e2790, 20, tag_7b7b08)
RESET_ARRAY(reset_0e27d0, 120, tag_7b7b08)
RESET_ARRAY(reset_0e2800, 136, tag_7b7b08)
RESET_ARRAY(reset_0e2840, 8, tag_7b7b08)
RESET_ARRAY(reset_0e29d0, 4, tag_7b7b08)
RESET_ARRAY(reset_0e2ee0, 16, tag_7b7b08)
RESET_ARRAY(reset_0e2f10, 28, tag_7b7b08)
RESET_ARRAY(reset_0e2f40, 8, tag_7b7b08)
RESET_ARRAY(reset_0e3630, 32, tag_7b7b08)
RESET_ARRAY(reset_0e36b0, 8, tag_7b7b08)
RESET_ARRAY(reset_0e3730, 4, tag_7b7b08)
RESET_ARRAY(reset_0e37b0, 16, tag_7b7b08)
RESET_ARRAY(reset_0e3830, 16, tag_7b7b08)
RESET_ARRAY(reset_0e3980, 64, tag_7b7b08)
RESET_ARRAY(reset_0e3a50, 4, tag_7b7b08)
RESET_ARRAY(reset_0e3c40, 76, tag_7b7b08)
RESET_ARRAY(reset_0e3d90, 16, tag_7b7b08)
RESET_ARRAY(reset_0e3e10, 12, tag_7b7b08)
RESET_ARRAY(reset_0e43c0, 16, tag_7b7b08)
RESET_ARRAY(reset_0e4430, 16, tag_7b7b08)
RESET_ARRAY(reset_132130, 4, tag_7b7b08)
RESET_ARRAY(reset_13efe0, 4, tag_7b7b08)
RESET_ARRAY(reset_1486d0, 4, tag_7b7b08)
RESET_ARRAY(reset_150ef0, 8, tag_7b7b08)
RESET_ARRAY(reset_150f70, 16, tag_7b7b08)
RESET_ARRAY(reset_151060, 72, tag_7b7b08)
RESET_ARRAY(reset_1510f0, 12, tag_7b7b08)
RESET_ARRAY(reset_1511f0, 16, tag_7b7b08)
RESET_ARRAY(reset_151270, 28, tag_7b7b08)
RESET_ARRAY(reset_1512f0, 16, tag_7b7b08)
RESET_ARRAY(reset_154e10, 28, tag_7b7b08)
RESET_ARRAY(reset_15f390, 60, tag_7b7b08)
RESET_ARRAY(reset_15f550, 16, tag_7b7b08)
RESET_ARRAY(reset_16cbd0, 4, tag_7b7b08)
RESET_ARRAY(reset_16cc50, 4, tag_7b7b08)
RESET_ARRAY(reset_16ccd0, 40, tag_7b7b08)
RESET_ARRAY(reset_16cd60, 8, tag_7b7b08)
RESET_ARRAY(reset_16cde0, 12, tag_7b7b08)
RESET_ARRAY(reset_16ce70, 8, tag_7b7b08)
RESET_ARRAY(reset_172cb0, 24, tag_7b7b08)
RESET_ARRAY(reset_1ae6a0, 4, tag_7b7b08)
RESET_ARRAY(reset_1c6b50, 4, tag_7b7b08)
RESET_ARRAY(reset_1cee80, 4, tag_7b7b08)
RESET_ARRAY(reset_1e0e40, 4, tag_7db4cc)
RESET_ARRAY(reset_1ea810, 8, tag_7b7b08)
RESET_ARRAY(reset_1ea960, 56, tag_7b7b08)
RESET_ARRAY(reset_236270, 4, tag_7b7b08)
RESET_ARRAY(reset_26f4f0, 4, tag_7b7b08)
RESET_ARRAY(reset_2ccdc0, 4, tag_7b7b08)
RESET_ARRAY(reset_3738b0, 60, tag_7b7b08)
RESET_ARRAY(reset_3738e0, 40, tag_7b7b08)
RESET_ARRAY(reset_3739a0, 64, tag_7b7b08)
