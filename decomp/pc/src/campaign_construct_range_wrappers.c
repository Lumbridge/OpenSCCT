struct Record16 { unsigned int word[4]; };
struct Vector16;
struct Position16 { struct Record16 *ptr; };
struct Count16 { unsigned int value; };
/* The stock caller loads the record argument twice for the construction helper. */
struct RecordRef16 { const struct Record16 *ptr; };

#define CONSTRUCT_N(Entry, Fill) \
    struct Record16 *__fastcall construct_n_##Entry(struct Vector16 *vector, struct Position16 out, \
                                              struct Count16 count, volatile struct RecordRef16 item) \
    { \
        Fill(out.ptr, count.value, item, vector, item.ptr); \
        return out.ptr + count.value; \
    }

extern void __cdecl construct_copies_21d410(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_21d480(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_21d4f0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_21d560(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_21d5d0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);

CONSTRUCT_N(r21d8d0, construct_copies_21d410)
CONSTRUCT_N(r21d900, construct_copies_21d480)
CONSTRUCT_N(r21d930, construct_copies_21d4f0)
CONSTRUCT_N(r21d960, construct_copies_21d560)
CONSTRUCT_N(r21d990, construct_copies_21d5d0)
