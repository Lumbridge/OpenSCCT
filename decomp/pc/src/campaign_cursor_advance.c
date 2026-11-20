struct CursorState {
    unsigned reserved;
    unsigned char *current;
    unsigned count;
    unsigned tail;
};

struct CursorOwner {
    unsigned prefix[2];
    struct CursorState *after_state;
};

#define ADVANCE_CURSOR(Stride) \
    unsigned char *__fastcall advance_##Stride(struct CursorOwner *owner) \
    { \
        struct CursorState *state = owner->after_state - 1; \
        unsigned char *previous; \
        ++state->count; \
        previous = state->current; \
        state->current = previous + Stride; \
        return previous; \
    }

ADVANCE_CURSOR(8)
ADVANCE_CURSOR(12)
ADVANCE_CURSOR(16)
ADVANCE_CURSOR(24)
ADVANCE_CURSOR(28)
ADVANCE_CURSOR(36)
ADVANCE_CURSOR(40)
ADVANCE_CURSOR(52)
ADVANCE_CURSOR(64)
ADVANCE_CURSOR(80)
ADVANCE_CURSOR(84)
ADVANCE_CURSOR(88)
ADVANCE_CURSOR(92)
ADVANCE_CURSOR(192)
ADVANCE_CURSOR(204)
ADVANCE_CURSOR(208)
