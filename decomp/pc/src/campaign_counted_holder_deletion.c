/* Holder of an intrusively counted object, released through a sized category allocator.
   VC 7.1 C __fastcall stack-passes float and aggregate parameters; IntegerStackWord carries
   the original unsigned integer words through those stack slots without numeric conversion. */
typedef union { unsigned int value; float stack_word; } IntegerStackWord;
struct Counted;
struct CountedMethods { void (__fastcall *destroy)(struct Counted *, float free_memory); };
struct Counted { const struct CountedMethods *methods; unsigned short id; unsigned short references; };
struct Allocation { void *object; };
struct Flags { unsigned value; };
struct Manager;
struct ManagerMethods { void *reserved[5]; void (__fastcall *release)(struct Manager *, struct Allocation, float size, float category); };
struct Manager { const struct ManagerMethods *methods; };
struct Holder { const void *methods; unsigned short size; unsigned short pad; struct Counted *object; };
extern const void *const holder_methods[], *const base_methods[];
extern struct Manager *manager;
#define DELETE_COUNTED_HOLDER(Category) struct Holder *__fastcall delete_holder_##Category(struct Holder *self, struct Flags flags) {     struct Counted *object = self->object;     self->methods = holder_methods;     if (--object->references == 0) {         static const IntegerStackWord free_memory = { 1 };         object->methods->destroy(object, free_memory.stack_word);     }     self->methods = base_methods;     if (flags.value & 1) {         static const IntegerStackWord category = { Category };         struct Allocation allocation;         IntegerStackWord size;         allocation.object = self;         size.value = self->size;         manager->methods->release(manager, allocation, size.stack_word, category.stack_word);     }     return self; }

DELETE_COUNTED_HOLDER(6)
DELETE_COUNTED_HOLDER(21)

/* The same holder when the counted object may be absent. */
#define DELETE_OPTIONAL_HOLDER(Category) \
struct Holder *__fastcall delete_optional_holder_##Category(struct Holder *self, struct Flags flags) \
{ \
    struct Counted *object = self->object; \
    self->methods = holder_methods; \
    if (object) { \
        object->references--; \
        if (object->references == 0) { \
            static const IntegerStackWord free_memory = { 1 }; \
            object->methods->destroy(object, free_memory.stack_word); \
        } \
    } \
    self->methods = base_methods; \
    if (flags.value & 1) { \
        static const IntegerStackWord category = { Category }; \
        struct Allocation allocation; \
        IntegerStackWord size; \
        allocation.object = self; \
        size.value = self->size; \
        manager->methods->release(manager, allocation, size.stack_word, category.stack_word); \
    } \
    return self; \
}

DELETE_OPTIONAL_HOLDER(21)
