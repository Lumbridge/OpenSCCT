struct Object { const void *methods; };
extern const void *const object_methods[];
struct Allocation { struct Object *object; };
struct DeleteFlags { unsigned value; };
struct Allocator;
struct Methods {
    void *reserved[2];
    void (__fastcall *release)(struct Allocator *, struct Allocation);
};
struct Allocator { const struct Methods *methods; };
extern struct Allocator *allocator;
extern void __fastcall destruct(struct Object *);

extern void __fastcall destruct_base(struct Object *);

struct Object *__fastcall delete_derived(struct Object *object, struct DeleteFlags flags)
{
    object->methods = object_methods;
    destruct(object);
    destruct_base(object);
    if (flags.value & 1) {
        struct Allocation allocation;
        allocation.object = object;
        allocator->methods->release(allocator, allocation);
    }
    return object;
}
