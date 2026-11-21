/* Object that owns a COM-style interface and keeps a use count; the interface is
   released and cleared before the base destructor runs. */
struct Interface;
struct InterfaceMethods { void *query; void *add_ref; unsigned long (__stdcall *release)(struct Interface *); };
struct Interface { const struct InterfaceMethods *methods; };
struct Allocation { void *object; };
struct Flags { unsigned value; };
struct Allocator;
struct AllocatorMethods { void *reserved[2]; void (__fastcall *release)(struct Allocator *, struct Allocation); };
struct Allocator { const struct AllocatorMethods *methods; };
struct Object {
    const void *methods;
    unsigned fields_04[3];
    int uses;
    unsigned fields_14[4];
    struct Interface *resource;
};
extern const void *const object_methods[];
extern struct Allocator *allocator;
extern void __fastcall destroy_base(struct Object *);
struct Object *__fastcall delete_object(struct Object *self, struct Flags flags)
{
    self->methods = object_methods;
    self->uses--;
    if (self->resource) {
        self->resource->methods->release(self->resource);
        self->resource = 0;
    }
    destroy_base(self);
    if (flags.value & 1) {
        struct Allocation allocation;
        allocation.object = self;
        allocator->methods->release(allocator, allocation);
    }
    return self;
}
