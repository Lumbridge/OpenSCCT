struct Array { unsigned *data; int size; unsigned capacity_flags; };
extern void __cdecl grow_array(struct Array *array, unsigned element_size);

#define APPEND_METHOD(Offset) \
    struct Owner##Offset { unsigned char prefix[Offset]; struct Array array; }; \
    void __fastcall append_##Offset(struct Owner##Offset *owner, unsigned unused_edx, unsigned value) \
    { \
        struct Array *array = &owner->array; \
        if (array->size == (int)(array->capacity_flags & 0x7fffffffU)) \
            grow_array(array, sizeof(value)); \
        array->data[array->size++] = value; \
    }

APPEND_METHOD(8)
APPEND_METHOD(40)
APPEND_METHOD(52)
APPEND_METHOD(60)
APPEND_METHOD(72)
APPEND_METHOD(84)
APPEND_METHOD(96)
APPEND_METHOD(108)
APPEND_METHOD(112)
APPEND_METHOD(120)
APPEND_METHOD(124)
APPEND_METHOD(132)
APPEND_METHOD(136)
APPEND_METHOD(144)
APPEND_METHOD(148)
APPEND_METHOD(156)
APPEND_METHOD(168)
APPEND_METHOD(180)
APPEND_METHOD(224)
APPEND_METHOD(228)
APPEND_METHOD(236)
APPEND_METHOD(248)
APPEND_METHOD(260)
APPEND_METHOD(272)
