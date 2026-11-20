struct Size { unsigned value; };
extern void *__cdecl allocate_buffer(unsigned, unsigned);
extern void __cdecl release_buffer(void *);
struct Element4 { unsigned char bytes[4]; };
struct Vector4 { unsigned reserved; struct Element4 *first, *end, *limit; };
struct Pointer4 { struct Element4 *value; };
extern unsigned kind_a17b1c;
extern unsigned kind_a17c38;
extern unsigned kind_a17dd8;
extern unsigned kind_a1a2e4;
extern void __fastcall copy_430a20(struct Vector4 *, struct Pointer4, struct Pointer4, struct Pointer4);
extern void __fastcall copy_4368f0(struct Vector4 *, struct Pointer4, struct Pointer4, struct Pointer4);
extern void __fastcall copy_448da0(struct Vector4 *, struct Pointer4, struct Pointer4, struct Pointer4);
extern void __fastcall copy_448dd0(struct Vector4 *, struct Pointer4, struct Pointer4, struct Pointer4);
extern void __fastcall copy_463010(struct Vector4 *, struct Pointer4, struct Pointer4, struct Pointer4);

#define RESERVE_VECTOR(Entry, SizeBytes, Kind, Copy) \
    void __fastcall Entry(struct Vector##SizeBytes *v, struct Size wanted) \
    { \
        if (wanted.value <= ~0u / sizeof(struct Element##SizeBytes) && \
            (!v->first ? 0 : (unsigned)(v->limit - v->first)) < wanted.value) { \
            unsigned count; \
            struct Pointer##SizeBytes first, end, storage; \
            storage.value = allocate_buffer(wanted.value * sizeof(struct Element##SizeBytes), Kind); \
            end.value = v->end; \
            first.value = v->first; \
            Copy(v, first, end, storage); \
            count = !v->first ? 0 : (unsigned)(v->end - v->first); \
            if (v->first) release_buffer(v->first); \
            v->limit = storage.value + wanted.value; \
            v->end = storage.value + count; \
            v->first = storage.value; \
        } \
    }

RESERVE_VECTOR(reserve_430c20, 4, kind_a17b1c, copy_430a20)
RESERVE_VECTOR(reserve_436d50, 4, kind_a17c38, copy_4368f0)
RESERVE_VECTOR(reserve_449fa0, 4, kind_a17dd8, copy_448da0)
RESERVE_VECTOR(reserve_44a030, 4, kind_a17dd8, copy_448dd0)
RESERVE_VECTOR(reserve_4631f0, 4, kind_a1a2e4, copy_463010)
