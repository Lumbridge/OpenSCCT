/* Objects that own one engine array {data, count, capacity}: remove every element,
   release the storage through the allocator, clear the array, then release the object.
   VC 7.1 C __fastcall stack-passes float and aggregate parameters; IntegerStackWord
   carries the original unsigned integer words through those stack slots. */
typedef union { unsigned int value; float stack_word; } IntegerStackWord;
struct Array { void *data; int count; int capacity; };
struct Allocation { void *object; };
struct Flags { unsigned value; };
struct Allocator;
struct AllocatorMethods { void *reserved[2]; void (__fastcall *release)(struct Allocator *, struct Allocation); };
struct Allocator { const struct AllocatorMethods *methods; };
extern struct Allocator *allocator;
extern void __fastcall remove_items(struct Array *, float index, float count);
extern void __fastcall remove_sized_items(struct Array *, float index, float count, float element_size);
extern void __fastcall destroy_base(void *);

static __forceinline void release_storage(struct Array *items)
{
    if (items->data) {
        struct Allocation allocation;
        allocation.object = items->data;
        allocator->methods->release(allocator, allocation);
    }
    items->data = 0;
    items->capacity = 0;
    items->count = 0;
}

static __forceinline void release_object(void *self)
{
    struct Allocation allocation;
    allocation.object = self;
    allocator->methods->release(allocator, allocation);
}

/* Offset is the hexadecimal position of the array member. The array is the last
   member destroyed; no base destructor follows. */
#define DELETE_ARRAY_OWNER(Offset) \
struct ArrayOwner_##Offset { unsigned char head[0x##Offset]; struct Array items; }; \
struct ArrayOwner_##Offset *__fastcall delete_array_owner_##Offset(struct ArrayOwner_##Offset *self, struct Flags flags) \
{ \
    struct Array *items = &self->items; \
    static const IntegerStackWord first = { 0 }; \
    IntegerStackWord count; \
    count.value = items->count; \
    remove_items(items, first.stack_word, count.stack_word); \
    release_storage(items); \
    if (flags.value & 1) release_object(self); \
    return self; \
}

/* A typed array member followed by the base destructor. */
#define DELETE_DERIVED_ARRAY_OWNER(Offset, ElementSize) \
struct DerivedArrayOwner_##Offset##_##ElementSize { unsigned char head[0x##Offset]; struct Array items; }; \
struct DerivedArrayOwner_##Offset##_##ElementSize *__fastcall delete_derived_array_owner_##Offset##_##ElementSize(struct DerivedArrayOwner_##Offset##_##ElementSize *self, struct Flags flags) \
{ \
    struct Array *items = &self->items; \
    static const IntegerStackWord first = { 0 }, element_size = { ElementSize }; \
    IntegerStackWord count; \
    count.value = items->count; \
    remove_sized_items(items, first.stack_word, count.stack_word, element_size.stack_word); \
    release_storage(items); \
    destroy_base(self); \
    if (flags.value & 1) release_object(self); \
    return self; \
}

DELETE_ARRAY_OWNER(70)
DELETE_ARRAY_OWNER(08)
DELETE_ARRAY_OWNER(24)
DELETE_DERIVED_ARRAY_OWNER(0c, 1)
DELETE_DERIVED_ARRAY_OWNER(0c, 4)
DELETE_DERIVED_ARRAY_OWNER(40, 4)
