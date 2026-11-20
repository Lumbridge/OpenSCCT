struct CursorState {
    unsigned reserved;
    unsigned char *current;
    unsigned count;
    unsigned tail;
};

extern struct CursorState *after_state;
extern unsigned char cursor_enabled;
extern void *__cdecl allocate_fallback(unsigned size, unsigned flags);

#define ALLOCATE_ELEMENT(Stride) \
    void *__cdecl allocate_##Stride(unsigned size) \
    { \
        if (cursor_enabled) { \
            struct CursorState *state = after_state - 1; \
            unsigned char *previous; \
            ++state->count; \
            previous = state->current; \
            state->current = previous + Stride; \
            return previous; \
        } \
        return allocate_fallback(size, 0); \
    }

ALLOCATE_ELEMENT(8)
ALLOCATE_ELEMENT(12)
ALLOCATE_ELEMENT(16)
ALLOCATE_ELEMENT(24)
ALLOCATE_ELEMENT(28)
ALLOCATE_ELEMENT(40)
ALLOCATE_ELEMENT(52)
ALLOCATE_ELEMENT(64)
ALLOCATE_ELEMENT(92)
