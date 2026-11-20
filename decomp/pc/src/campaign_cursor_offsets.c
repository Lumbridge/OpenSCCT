#define CURSOR_TYPE(Stride) \
    struct Element##Stride { unsigned char storage[Stride]; }; \
    struct Cursor##Stride { struct Element##Stride *position; };

#define CURSOR_RETREAT(Stride) \
    struct Cursor##Stride *__fastcall retreat_##Stride(struct Cursor##Stride *cursor, \
                                                      unsigned unused_edx, int count) \
    { \
        cursor->position += (int)(0u - (unsigned)count); \
        return cursor; \
    }

#define CURSOR_OFFSET(Stride) \
    struct Cursor##Stride *__fastcall offset_##Stride(const struct Cursor##Stride *cursor, \
                                                     unsigned unused_edx, struct Cursor##Stride *result, int count) \
    { \
        result->position = cursor->position + count; \
        return result; \
    }

#define CURSOR_BEFORE(Stride) \
    struct Cursor##Stride *__fastcall before_##Stride(const struct Cursor##Stride *cursor, \
                                                     unsigned unused_edx, struct Cursor##Stride *result, int count) \
    { \
        result->position = cursor->position + (int)(0u - (unsigned)count); \
        return result; \
    }

CURSOR_TYPE(1)
CURSOR_TYPE(4)
CURSOR_TYPE(8)
CURSOR_TYPE(12)
CURSOR_TYPE(16)
CURSOR_TYPE(32)
CURSOR_TYPE(60)

CURSOR_OFFSET(1)
CURSOR_BEFORE(4)
CURSOR_OFFSET(4)
CURSOR_RETREAT(4)
CURSOR_BEFORE(8)
CURSOR_OFFSET(8)
CURSOR_RETREAT(8)
CURSOR_OFFSET(12)
CURSOR_BEFORE(16)
CURSOR_OFFSET(16)
CURSOR_RETREAT(16)
CURSOR_OFFSET(32)
CURSOR_BEFORE(60)
CURSOR_OFFSET(60)
CURSOR_RETREAT(60)
