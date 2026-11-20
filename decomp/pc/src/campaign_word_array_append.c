struct word_array { unsigned int *data; int count; unsigned int capacity_and_flags; };
struct item_argument { const unsigned int *source; };
extern void __cdecl reserve_words(struct word_array *array, int item_size);
static __forceinline void append_word(struct word_array *array, struct item_argument item)
{
    if (array->count == (int)(array->capacity_and_flags & 0x7fffffffu))
        reserve_words(array, sizeof(unsigned int));
    array->data[array->count] = *item.source;
    array->count++;
}

/* Each entry is a separately emitted four-byte element append operation. */
#define APPEND_WORD(Entry) \
    void __fastcall append_word_##Entry(struct word_array *array, struct item_argument item) \
    { append_word(array, item); }

APPEND_WORD(r08abb0)
APPEND_WORD(r1b8050)
APPEND_WORD(r4fd440)
APPEND_WORD(r4ff4c0)
APPEND_WORD(r4ff500)
APPEND_WORD(r4ff540)
APPEND_WORD(r4ff580)
APPEND_WORD(r503a10)
APPEND_WORD(r505850)
APPEND_WORD(r50dfb0)
APPEND_WORD(r54ffa0)
APPEND_WORD(r54ffe0)
APPEND_WORD(r550020)
APPEND_WORD(r550060)
APPEND_WORD(r5500a0)
APPEND_WORD(r552e10)
APPEND_WORD(r552e50)
APPEND_WORD(r552e90)
APPEND_WORD(r552ed0)
APPEND_WORD(r552f10)
APPEND_WORD(r552f50)
APPEND_WORD(r552f90)
APPEND_WORD(r552fd0)
APPEND_WORD(r553010)
APPEND_WORD(r553050)
APPEND_WORD(r553090)
APPEND_WORD(r553220)
APPEND_WORD(r553310)
APPEND_WORD(r553350)
APPEND_WORD(r553480)
APPEND_WORD(r570250)
APPEND_WORD(r5782b0)
APPEND_WORD(r57ce40)
APPEND_WORD(r57ce80)
APPEND_WORD(r57cec0)
APPEND_WORD(r57ddd0)
APPEND_WORD(r57f0c0)
APPEND_WORD(r57f4f0)
APPEND_WORD(r57f530)
APPEND_WORD(r57fba0)
APPEND_WORD(r57fbe0)
APPEND_WORD(r583220)
APPEND_WORD(r583c30)
APPEND_WORD(r5843d0)
APPEND_WORD(r586910)
APPEND_WORD(r5885f0)
APPEND_WORD(r5891e0)
APPEND_WORD(r593b90)
APPEND_WORD(r593cc0)
APPEND_WORD(r6f42f0)
APPEND_WORD(r719030)
APPEND_WORD(r719070)
APPEND_WORD(r71ceb0)
APPEND_WORD(r71d370)
APPEND_WORD(r7931f0)
