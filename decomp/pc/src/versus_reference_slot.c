struct SharedObject;
/* The x86 virtual destructor takes its delete flag in a four-byte stack slot.
   This fastcall declaration passes the bit pattern 1 in that slot. */
#define DELETE_STORAGE_BITS 1.40129846e-45f
typedef void (__fastcall *Destroy)(struct SharedObject *, float);
struct SharedObject {
    Destroy *methods;
    unsigned references;
};

void __fastcall release_slot(struct SharedObject **slot)
{
    struct SharedObject *object = *slot;
    if (object) {
        unsigned references = object->references;
        if (references <= 1)
            object->methods[0](object, DELETE_STORAGE_BITS);
        else
            object->references = references - 1;
    }
}
