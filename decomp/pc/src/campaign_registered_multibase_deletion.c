/* Object with three method tables; an optional registration flag precedes the base destructor. */
struct Allocator;
struct Allocation { void *object; };
struct Flags { unsigned value; };
struct AllocatorMethods { void *reserved[2]; void (__fastcall *release)(struct Allocator *, struct Allocation); };
struct Allocator { const struct AllocatorMethods *methods; };
struct Object {
    const void *methods;
    unsigned field_04;
    const void *second_methods;
    unsigned field_0c;
    const void *third_methods;
    unsigned fields[10];
    unsigned char registered;
};
extern const void *const object_methods[], *const second_methods[], *const third_methods[];
extern struct Allocator *allocator;
extern void __fastcall unregister_object(struct Object *);
extern void __fastcall destroy_base(struct Object *);
struct Object *__fastcall delete_object(struct Object *self, struct Flags flags)
{
    self->methods = object_methods;
    self->second_methods = second_methods;
    self->third_methods = third_methods;
    if (self->registered) unregister_object(self);
    destroy_base(self);
    if (flags.value & 1) {
        struct Allocation allocation;
        allocation.object = self;
        allocator->methods->release(allocator, allocation);
    }
    return self;
}
