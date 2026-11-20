#include "campaign_fsarray.h"

extern void __fastcall call_resize_fsarray(struct FSArray *array, float stride_word);
struct WordRef { const unsigned int *word; };

#define APPEND_WORD(Entry) \
    int __fastcall Entry(struct FSArray *array, struct WordRef item) \
    { \
        static const union Stride stride = { sizeof(unsigned int) }; \
        int index = array->count++; \
        call_resize_fsarray(array, stride.stack_word); \
        ((unsigned int *)array->buffer.bytes)[index] = *item.word; \
        return index; \
    }

APPEND_WORD(append_fsarray_word_07fd30)
APPEND_WORD(append_fsarray_word_0b7b60)
APPEND_WORD(append_fsarray_word_0d7750)
