struct Record16 { unsigned int word[4]; };
struct Vector16 { void *allocator; struct Record16 *begin, *end, *limit; };
struct Position16 { struct Record16 *ptr; };
struct RecordRef16 { const struct Record16 *ptr; };
/* VC7.1 passes a float word on the stack in this member-call adapter. */
union CountWord { int value; float argument; };

#define APPEND(Entry, Insert) \
    void __fastcall append_##Entry(struct Vector16 *vector, struct RecordRef16 item) \
    { \
        static const union CountWord one = { 1 }; \
        unsigned int size = !vector->begin ? 0 : (unsigned int)(vector->end - vector->begin); \
        unsigned int capacity = !vector->begin ? 0 : (unsigned int)(vector->limit - vector->begin); \
        if (size < capacity) { \
            struct Record16 *last = vector->end; \
            if (last) *last = *item.ptr; \
            vector->end = last + 1; \
        } else { \
            struct Position16 last; \
            last.ptr = vector->end; \
            Insert(vector, last, one.argument, item); \
        } \
    }

extern void __fastcall insert_copies_444680(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_444890(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_444aa0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_444cb0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_444ec0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_4450d0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_4452e0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_4454f0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_445700(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_445910(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_445b20(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_445d30(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_445f40(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_446150(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_446360(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_446570(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_446780(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_446990(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_446ba0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_446db0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_446fc0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_4471d0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_4473e0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_4475f0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_447800(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_447a10(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_447c20(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_447e30(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_448040(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_448250(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_448460(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_448670(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_448880(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_448a90(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_4498a0(struct Vector16 *, struct Position16, float, struct RecordRef16);
extern void __fastcall insert_copies_449ab0(struct Vector16 *, struct Position16, float, struct RecordRef16);

APPEND(r44a0e0, insert_copies_444680)
APPEND(r44a150, insert_copies_444890)
APPEND(r44a1c0, insert_copies_444aa0)
APPEND(r44a230, insert_copies_444cb0)
APPEND(r44a2a0, insert_copies_444ec0)
APPEND(r44a310, insert_copies_4450d0)
APPEND(r44a380, insert_copies_4452e0)
APPEND(r44a3f0, insert_copies_4454f0)
APPEND(r44a460, insert_copies_445700)
APPEND(r44a4d0, insert_copies_445910)
APPEND(r44a540, insert_copies_445b20)
APPEND(r44a5b0, insert_copies_445d30)
APPEND(r44a620, insert_copies_445f40)
APPEND(r44a690, insert_copies_446150)
APPEND(r44a700, insert_copies_446360)
APPEND(r44a770, insert_copies_446570)
APPEND(r44a7e0, insert_copies_446780)
APPEND(r44a850, insert_copies_446990)
APPEND(r44a8c0, insert_copies_446ba0)
APPEND(r44a930, insert_copies_446db0)
APPEND(r44a9a0, insert_copies_446fc0)
APPEND(r44aa10, insert_copies_4471d0)
APPEND(r44aa80, insert_copies_4473e0)
APPEND(r44aaf0, insert_copies_4475f0)
APPEND(r44ab60, insert_copies_447800)
APPEND(r44abd0, insert_copies_447a10)
APPEND(r44ac40, insert_copies_447c20)
APPEND(r44acb0, insert_copies_447e30)
APPEND(r44ad20, insert_copies_448040)
APPEND(r44ad90, insert_copies_448250)
APPEND(r44ae00, insert_copies_448460)
APPEND(r44ae70, insert_copies_448670)
APPEND(r44aee0, insert_copies_448880)
APPEND(r44af50, insert_copies_448a90)
APPEND(r44bbb0, insert_copies_4498a0)
APPEND(r44bc20, insert_copies_449ab0)
