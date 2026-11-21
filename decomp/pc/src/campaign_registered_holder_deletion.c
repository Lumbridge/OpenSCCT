/* Registered storage object that also holds an intrusively counted object.
   VC 7.1 C __fastcall stack-passes float and aggregate parameters; IntegerStackWord
   carries the original unsigned integer words through those stack slots. */
typedef union { unsigned int value; float stack_word; } IntegerStackWord;
struct Counted;
struct CountedMethods { void (__fastcall *destroy)(struct Counted *, float free_memory); };
struct Counted { const struct CountedMethods *methods; unsigned short id; unsigned short references; };
struct Allocation { void *object; };
struct Flags { unsigned value; };
struct Manager;
struct ManagerMethods { void *reserved[5]; void (__fastcall *release)(struct Manager *, struct Allocation, float size, float category); };
struct Manager { const struct ManagerMethods *methods; };
struct Holder {
    const void *methods;
    unsigned short size_flags;
    unsigned short pad;
};
extern const void *const holder_methods[], *const registered_methods[], *const base_methods[];
extern struct Manager *manager;
extern void __fastcall set_registration(struct Holder *, float registered);

/* Offset is the hexadecimal position of the counted-object pointer. */
#define DELETE_REGISTERED_HOLDER(Offset, Category) \
struct RegisteredHolder_##Offset { struct Holder base; unsigned char fields[0x##Offset - 8]; struct Counted *object; }; \
struct Holder *__fastcall delete_registered_holder_##Offset(struct Holder *self, struct Flags flags) \
{ \
    struct Counted *object = ((struct RegisteredHolder_##Offset *)self)->object; \
    self->methods = holder_methods; \
    if (--object->references == 0) { \
        static const IntegerStackWord free_memory = { 1 }; \
        object->methods->destroy(object, free_memory.stack_word); \
    } \
    self->methods = registered_methods; \
    if (self->size_flags & 0x8000u) { \
        static const IntegerStackWord unregistered = { 0 }; \
        self->size_flags &= 0x7fffu; \
        set_registration(self, unregistered.stack_word); \
    } \
    self->methods = base_methods; \
    if (flags.value & 1) { \
        static const IntegerStackWord category = { Category }; \
        struct Allocation allocation; \
        IntegerStackWord size; \
        allocation.object = self; \
        size.value = self->size_flags; \
        manager->methods->release(manager, allocation, size.stack_word, category.stack_word); \
    } \
    return self; \
}

DELETE_REGISTERED_HOLDER(0c, 34)
DELETE_REGISTERED_HOLDER(30, 34)
