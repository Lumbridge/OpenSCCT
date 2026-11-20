struct SharedObject;
/* The x86 virtual destructor takes its delete flag in a four-byte stack slot.
   This fastcall declaration passes the bit pattern 1 in that slot. */
#define DELETE_STORAGE_BITS 1.40129846e-45f
typedef void (__fastcall *Destroy)(struct SharedObject *, float);
struct SharedObject {
    Destroy *methods;
    unsigned references;
};

static __forceinline void assign(struct SharedObject **destination, struct SharedObject *const *source)
{
    struct SharedObject *old = *destination;
    if (*source != old) {
        struct SharedObject *value;
        if (old) {
            unsigned count = old->references;
            if (count <= 1)
                old->methods[0](old, DELETE_STORAGE_BITS);
            else
                old->references = count - 1;
        }
        value = *source;
        *destination = value;
        if (value)
            ++value->references;
    }
}

void __cdecl assign_range(struct SharedObject **first, struct SharedObject **last,
                          struct SharedObject *const *source)
{
    while (first != last)
        assign(first++, source);
}

struct SharedObject ***__cdecl copy_backward(struct SharedObject ***result, struct SharedObject **first,
                                           struct SharedObject **last, struct SharedObject **destination)
{
    while (first != last)
        assign(--destination, --last);
    *result = destination;
    return result;
}
