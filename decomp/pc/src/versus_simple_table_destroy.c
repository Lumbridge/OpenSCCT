struct Object { const void *methods; };
struct Flags { unsigned value; };

extern const void *const methods_2e0b90[];
extern const void *const methods_2e0b98[];
extern const void *const methods_2f149c[];
extern void __cdecl release_150880(void *);

#define DESTROY(Entry, Methods, Release) \
    struct Object *__fastcall Entry(struct Object *self, struct Flags flags) \
    { \
        self->methods = Methods; \
        if (flags.value & 1) \
            Release(self); \
        return self; \
    }

DESTROY(destroy_2266c0, methods_2e0b90, release_150880)
DESTROY(destroy_226790, methods_2e0b98, release_150880)
DESTROY(destroy_2aabb0, methods_2f149c, release_150880)
