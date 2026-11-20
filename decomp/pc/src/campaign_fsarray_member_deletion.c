struct Allocation { void *object; };
struct Allocator;
struct AllocatorOps {
    void *reserved[2];
    void (__fastcall *free)(struct Allocator *, struct Allocation);
};
struct Allocator { const struct AllocatorOps *ops; };
extern struct Allocator *engine_allocator;
struct Array { void *data; unsigned count; };

struct Flags { unsigned value; };
/* These stack words preserve the integer argument bits for the x86 member call. */
union StackWord { unsigned value; float stack_word; };

extern void __fastcall remove_range_3cb3f0(struct Array *, float, float, float);

static __forceinline void free_allocation(void *pointer)
{
    struct Allocation allocation;
    allocation.object = pointer;
    engine_allocator->ops->free(engine_allocator, allocation);
}

#define DELETE_ARRAY_MEMBER(Entry, MemberOffset, ElementBytes, Remove) \
    void *__fastcall Entry(void *self, struct Flags flags) \
    { \
        static const union StackWord stride = { ElementBytes }; \
        union StackWord count; \
        struct Array *array = (struct Array *)((unsigned char *)self + MemberOffset); \
        count.value = array->count; \
        Remove(array, 0.0f, count.stack_word, stride.stack_word); \
        if (array->data) free_allocation(array->data); \
        array->data = 0; \
     \
        array->count = 0; \
        if (flags.value & 1) free_allocation(self); \
        return self; \
    }

DELETE_ARRAY_MEMBER(delete_array_member_081380, 4, 1, remove_range_3cb3f0)
DELETE_ARRAY_MEMBER(delete_array_member_0829b0, 36, 4, remove_range_3cb3f0)
