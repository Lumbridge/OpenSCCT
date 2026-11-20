/* Fixed-width records copied by value. The element identities are unknown. */

#define ASSIGN_RANGE(Entry, T) \
void __cdecl assign_range_##Entry(struct T *first, struct T *last, const struct T *value) \
{ \
    for (; first != last; ++first) { \
        *first = *value; \
    } \
}

#define CONSTRUCT_COPIES(Entry, T) \
void __cdecl construct_copies_##Entry(struct T *out, unsigned int count, const struct T *value, ...) \
{ \
    for (; count > 0; --count, ++out) { \
        if (out) *out = *value; \
    } \
}

#define CONSTRUCT_RANGE(Entry, T) \
struct T * __cdecl construct_range_##Entry(const struct T *first, const struct T *last, struct T *out, ...) \
{ \
    for (; first != last; ++first, ++out) { \
        if (out) *out = *first; \
    } \
    return out; \
}

#define COPY_BACKWARD(Entry, T) \
struct T * __cdecl copy_backward_##Entry(const struct T *first, const struct T *last, struct T *out, ...) \
{ \
    while (first != last) { \
        *--out = *--last; \
    } \
    return out; \
}
