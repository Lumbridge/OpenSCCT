struct Object;
struct Allocation { struct Object *object; };
struct DeleteFlags { unsigned value; };
struct Allocator;
struct Methods {
    void *reserved;
    void (__fastcall *release)(struct Allocator *, struct Allocation);
};
struct Allocator { const struct Methods *methods; };
extern struct Allocator *allocator;
extern void __fastcall destruct(struct Object *);

struct Object *__fastcall delete_from_allocator(struct Object *object, struct DeleteFlags flags)
{
    destruct(object);
    if (flags.value & 1) {
        struct Allocation allocation;
        allocation.object = object;
        allocator->methods->release(allocator, allocation);
    }
    return object;
}
