#include "campaign_fsarray.h"
extern void __fastcall call_resize_fsarray(struct FSArray *array, float stride_word);
extern void *__cdecl memset(void *destination, int value, unsigned int bytes);

#define APPEND(Entry, Record) \
    int __fastcall Entry(struct FSArray *array, struct Ref_##Record item) \
    { \
        static const union Stride stride = { sizeof(struct Record) }; \
        int index = array->count++; \
        call_resize_fsarray(array, stride.stack_word); \
        ((struct Record *)array->buffer.bytes)[index] = *item.ptr; \
        return index; \
    }

#define ADD_ZEROED(Entry, Record) \
    int __fastcall Entry(struct FSArray *array, struct ArraySize amount) \
    { \
        static const union Stride stride = { sizeof(struct Record) }; \
        int index = array->count; \
        array->count += amount.value; \
        call_resize_fsarray(array, stride.stack_word); \
        memset((struct Record *)array->buffer.bytes + index, 0, amount.value * sizeof(struct Record)); \
        return index; \
    }

struct Record12 { unsigned int words[3]; };
struct Ref_Record12 { const struct Record12 *ptr; };
struct Record16 { unsigned int words[4]; };
struct Ref_Record16 { const struct Record16 *ptr; };
struct Record36 { unsigned int words[9]; };
struct Ref_Record36 { const struct Record36 *ptr; };
struct Record60 { unsigned int words[15]; };
struct Ref_Record60 { const struct Record60 *ptr; };
struct Record120 { unsigned int words[30]; };
struct Ref_Record120 { const struct Record120 *ptr; };
struct Record128 { unsigned int words[32]; };
struct Ref_Record128 { const struct Record128 *ptr; };
struct Record156 { unsigned int words[39]; };
struct Ref_Record156 { const struct Record156 *ptr; };

ADD_ZEROED(add_zeroed_fsarray_0b5f80, Record36)
APPEND(append_fsarray_0b7ab0, Record12)
ADD_ZEROED(add_zeroed_fsarray_0cac60, Record16)
ADD_ZEROED(add_zeroed_fsarray_0cacb0, Record128)
ADD_ZEROED(add_zeroed_fsarray_0cad10, Record60)
APPEND(append_fsarray_0d7830, Record156)
ADD_ZEROED(add_zeroed_fsarray_179b50, Record120)
