struct Vector { unsigned reserved; void *first, *end, *limit; };
extern void *__cdecl get_manager(void);
extern void __cdecl release_buffer(void *, void *, unsigned);
#define RELEASE_VECTOR(Entry, Category) \
    void __fastcall Entry(struct Vector *vector) \
    { \
        if (vector->first) release_buffer(get_manager(), vector->first, Category); \
        vector->first = 0; \
        vector->end = 0; \
        vector->limit = 0; \
    }

RELEASE_VECTOR(clear_493030, 7)
RELEASE_VECTOR(clear_493260, 7)
RELEASE_VECTOR(clear_4932a0, 7)
RELEASE_VECTOR(clear_4933e0, 7)
RELEASE_VECTOR(clear_495240, 7)
RELEASE_VECTOR(clear_495490, 7)
RELEASE_VECTOR(clear_49c4c0, 7)
RELEASE_VECTOR(clear_49c620, 7)
RELEASE_VECTOR(clear_49c690, 7)
RELEASE_VECTOR(clear_4bc540, 7)
RELEASE_VECTOR(clear_4beb70, 7)
RELEASE_VECTOR(clear_4bec10, 7)
RELEASE_VECTOR(clear_4bec50, 7)
RELEASE_VECTOR(clear_4becc0, 7)
RELEASE_VECTOR(clear_4c7800, 7)
RELEASE_VECTOR(clear_4c7ab0, 7)
RELEASE_VECTOR(clear_4c7af0, 7)
RELEASE_VECTOR(clear_4c7b60, 7)
RELEASE_VECTOR(clear_73ab80, 7)
RELEASE_VECTOR(clear_73ac20, 7)
RELEASE_VECTOR(clear_73ad20, 7)
