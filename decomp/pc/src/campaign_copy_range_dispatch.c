struct Vector;
struct Position { void *ptr; };

extern void *__cdecl copy_21d170(void *, void *, struct Position, struct Vector *, void *);
extern void *__cdecl copy_21d1e0(void *, void *, struct Position, struct Vector *, void *);
extern void *__cdecl copy_21d250(void *, void *, struct Position, struct Vector *, void *);
extern void *__cdecl copy_21d2c0(void *, void *, struct Position, struct Vector *, void *);
extern void *__cdecl copy_21d330(void *, void *, struct Position, struct Vector *, void *);
extern void *__cdecl copy_385980(void *, void *, struct Position, struct Vector *, void *);
extern void *__cdecl copy_42b480(void *, void *, struct Position, struct Vector *, void *);
extern void *__cdecl copy_492f00(void *, void *, struct Position, struct Vector *, void *);
extern void *__cdecl copy_4949f0(void *, void *, struct Position, struct Vector *, void *);

#define COPY_RANGE(Entry, Copy) \
    void *__fastcall Entry(struct Vector *vector, struct Position first, \
                           struct Position last, volatile struct Position out) \
    { \
        return Copy(first.ptr, last.ptr, out, vector, out.ptr); \
    }

COPY_RANGE(range_21d830, copy_21d170)
COPY_RANGE(range_21d850, copy_21d1e0)
COPY_RANGE(range_21d870, copy_21d250)
COPY_RANGE(range_21d890, copy_21d2c0)
COPY_RANGE(range_21d8b0, copy_21d330)
COPY_RANGE(range_3882c0, copy_385980)
COPY_RANGE(range_42c230, copy_42b480)
COPY_RANGE(range_493360, copy_492f00)
COPY_RANGE(range_494ce0, copy_4949f0)
