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

extern void __cdecl construct_copies_441b10(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441b60(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441bb0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441c00(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441c50(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441ca0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441cf0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441d40(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441d90(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441de0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441e30(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441e80(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441ed0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441f10(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441f50(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441f90(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_441fd0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442020(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442070(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_4420c0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442110(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442160(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_4421b0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442200(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442250(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442290(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_4422d0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442320(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442360(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_4423a0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_4423f0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442440(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442490(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_4424e0(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442530(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);
extern void __cdecl construct_copies_442570(struct Record16 *, unsigned int, struct RecordRef16, struct Vector16 *, const struct Record16 *);

CONSTRUCT_N(r443e20, construct_copies_441b10)
CONSTRUCT_N(r443e60, construct_copies_441b60)
CONSTRUCT_N(r443ea0, construct_copies_441bb0)
CONSTRUCT_N(r443ee0, construct_copies_441c00)
CONSTRUCT_N(r443f20, construct_copies_441c50)
CONSTRUCT_N(r443f60, construct_copies_441ca0)
CONSTRUCT_N(r443fa0, construct_copies_441cf0)
CONSTRUCT_N(r443fe0, construct_copies_441d40)
CONSTRUCT_N(r444020, construct_copies_441d90)
CONSTRUCT_N(r444060, construct_copies_441de0)
CONSTRUCT_N(r4440a0, construct_copies_441e30)
CONSTRUCT_N(r4440e0, construct_copies_441e80)
CONSTRUCT_N(r444120, construct_copies_441ed0)
CONSTRUCT_N(r444150, construct_copies_441f10)
CONSTRUCT_N(r444180, construct_copies_441f50)
CONSTRUCT_N(r4441b0, construct_copies_441f90)
CONSTRUCT_N(r4441e0, construct_copies_441fd0)
CONSTRUCT_N(r444220, construct_copies_442020)
CONSTRUCT_N(r444260, construct_copies_442070)
CONSTRUCT_N(r4442a0, construct_copies_4420c0)
CONSTRUCT_N(r4442e0, construct_copies_442110)
CONSTRUCT_N(r444320, construct_copies_442160)
CONSTRUCT_N(r444360, construct_copies_4421b0)
CONSTRUCT_N(r4443a0, construct_copies_442200)
CONSTRUCT_N(r4443e0, construct_copies_442250)
CONSTRUCT_N(r444410, construct_copies_442290)
CONSTRUCT_N(r444440, construct_copies_4422d0)
CONSTRUCT_N(r444480, construct_copies_442320)
CONSTRUCT_N(r4444b0, construct_copies_442360)
CONSTRUCT_N(r4444e0, construct_copies_4423a0)
CONSTRUCT_N(r444520, construct_copies_4423f0)
CONSTRUCT_N(r444560, construct_copies_442440)
CONSTRUCT_N(r4445a0, construct_copies_442490)
CONSTRUCT_N(r4445e0, construct_copies_4424e0)
CONSTRUCT_N(r444620, construct_copies_442530)
CONSTRUCT_N(r444650, construct_copies_442570)
