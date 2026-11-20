#define CURSOR_TYPE(Stride) \
    struct Element##Stride { unsigned char storage[Stride]; }; \
    struct Cursor##Stride { struct Element##Stride *position; };

#define CURSOR_DISTANCE(Stride) \
    int __fastcall distance_##Stride(struct Cursor##Stride *left, unsigned unused_edx, \
                                     const struct Cursor##Stride *right) \
    { \
        return left->position - right->position; \
    }

#define CURSOR_ADVANCE(Stride) \
    struct Cursor##Stride *__fastcall advance_##Stride(struct Cursor##Stride *cursor, \
                                                      unsigned unused_edx, int count) \
    { \
        cursor->position += count; \
        return cursor; \
    }

CURSOR_TYPE(1)
CURSOR_TYPE(4)
CURSOR_TYPE(8)
CURSOR_TYPE(12)
CURSOR_TYPE(16)
CURSOR_TYPE(32)
CURSOR_TYPE(60)

CURSOR_ADVANCE(1)
CURSOR_ADVANCE(4)
CURSOR_DISTANCE(4)
CURSOR_ADVANCE(8)
CURSOR_DISTANCE(8)
CURSOR_ADVANCE(12)
CURSOR_DISTANCE(12)
CURSOR_ADVANCE(16)
CURSOR_DISTANCE(16)
CURSOR_ADVANCE(32)
CURSOR_DISTANCE(32)
CURSOR_ADVANCE(60)
