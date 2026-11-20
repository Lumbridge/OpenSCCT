#include "campaign_array_growth.h"
extern void *__cdecl memset(void *destination, int value, unsigned int size);
#pragma intrinsic(memset)

static __forceinline int add_zeroed(struct Array *array, struct ArraySize element, struct ArraySize amount,
                                    const unsigned short *allocation_tag)
{
    int first = extend_array(array, amount.value, element.value, allocation_tag);
    memset(array->buffer.bytes + first * element.value, 0, amount.value * element.value);
    return first;
}

#define ADD_ZEROED(Entry, Tag) \
    int __fastcall add_zeroed_##Entry(struct Array *array, struct ArraySize element, struct ArraySize amount) \
    { return add_zeroed(array, element, amount, Tag); }

extern const unsigned short array_add_tag_7b7b08[];
extern const unsigned short array_add_tag_7ba13c[];
extern const unsigned short array_add_tag_7bc158[];
extern const unsigned short array_add_tag_7bc180[];
extern const unsigned short array_add_tag_7be9a8[];
extern const unsigned short array_add_tag_7cddbc[];
extern const unsigned short array_add_tag_7cf82c[];

ADD_ZEROED(r051210, array_add_tag_7b7b08)
ADD_ZEROED(r0608b0, array_add_tag_7b7b08)
ADD_ZEROED(r077460, array_add_tag_7be9a8)
ADD_ZEROED(r081fb0, array_add_tag_7b7b08)
ADD_ZEROED(r09e130, array_add_tag_7b7b08)
ADD_ZEROED(r0bfa80, array_add_tag_7b7b08)
ADD_ZEROED(r0c6d40, array_add_tag_7b7b08)
ADD_ZEROED(r0d7aa0, array_add_tag_7bc158)
ADD_ZEROED(r0d7b20, array_add_tag_7bc180)
ADD_ZEROED(r0dd0b0, array_add_tag_7b7b08)
ADD_ZEROED(r0dd170, array_add_tag_7b7b08)
ADD_ZEROED(r0dd5a0, array_add_tag_7cf82c)
ADD_ZEROED(r0e34c0, array_add_tag_7b7b08)
ADD_ZEROED(r0e38b0, array_add_tag_7b7b08)
ADD_ZEROED(r0e3a80, array_add_tag_7b7b08)
ADD_ZEROED(r0e3ef0, array_add_tag_7b7b08)
ADD_ZEROED(r0e3f70, array_add_tag_7b7b08)
ADD_ZEROED(r0e4070, array_add_tag_7b7b08)
ADD_ZEROED(r1298d0, array_add_tag_7b7b08)
ADD_ZEROED(r12bb20, array_add_tag_7b7b08)
ADD_ZEROED(r130d70, array_add_tag_7cddbc)
ADD_ZEROED(r172b90, array_add_tag_7b7b08)
ADD_ZEROED(r1adcb0, array_add_tag_7b7b08)
ADD_ZEROED(r1ae7e0, array_add_tag_7b7b08)
ADD_ZEROED(r1aeae0, array_add_tag_7b7b08)
ADD_ZEROED(r1ccc30, array_add_tag_7b7b08)
ADD_ZEROED(r1f6db0, array_add_tag_7b7b08)
ADD_ZEROED(r23ac10, array_add_tag_7b7b08)
ADD_ZEROED(r23ac90, array_add_tag_7b7b08)
ADD_ZEROED(r2cce40, array_add_tag_7b7b08)
ADD_ZEROED(r2ccf00, array_add_tag_7b7b08)
ADD_ZEROED(r2d75d0, array_add_tag_7b7b08)
ADD_ZEROED(r2dbd90, array_add_tag_7b7b08)
ADD_ZEROED(r2dbeb0, array_add_tag_7b7b08)
ADD_ZEROED(r2dbf30, array_add_tag_7b7b08)
ADD_ZEROED(r2dbfb0, array_add_tag_7b7b08)
ADD_ZEROED(r2e85a0, array_add_tag_7b7b08)
ADD_ZEROED(r2f1920, array_add_tag_7b7b08)
ADD_ZEROED(r2f4450, array_add_tag_7b7b08)
ADD_ZEROED(r2f4510, array_add_tag_7b7b08)
ADD_ZEROED(r313220, array_add_tag_7b7b08)
ADD_ZEROED(r317ef0, array_add_tag_7b7b08)
ADD_ZEROED(r31eef0, array_add_tag_7b7b08)
ADD_ZEROED(r321b30, array_add_tag_7ba13c)
ADD_ZEROED(r3221b0, array_add_tag_7b7b08)
ADD_ZEROED(r325b70, array_add_tag_7b7b08)
ADD_ZEROED(r325bf0, array_add_tag_7b7b08)
ADD_ZEROED(r326e10, array_add_tag_7b7b08)
ADD_ZEROED(r328ae0, array_add_tag_7b7b08)
ADD_ZEROED(r328b60, array_add_tag_7b7b08)
ADD_ZEROED(r328be0, array_add_tag_7b7b08)
ADD_ZEROED(r328c60, array_add_tag_7b7b08)
ADD_ZEROED(r328ce0, array_add_tag_7b7b08)
ADD_ZEROED(r344f50, array_add_tag_7b7b08)
ADD_ZEROED(r3465d0, array_add_tag_7b7b08)
ADD_ZEROED(r34f270, array_add_tag_7b7b08)
ADD_ZEROED(r357700, array_add_tag_7b7b08)
ADD_ZEROED(r3dc090, array_add_tag_7b7b08)
