struct Allocation { void *object; };
struct Allocator;
struct AllocatorOps {
    void *reserved[2];
    void (__fastcall *free)(struct Allocator *, struct Allocation);
};
struct Allocator { const struct AllocatorOps *ops; };
extern struct Allocator *engine_allocator;
struct Array { void *data; unsigned count, capacity; };

struct Flags { unsigned value; };
/* These stack words preserve the integer argument bits for the x86 member call. */
union StackWord { unsigned value; float stack_word; };

extern void __fastcall remove_range_007710(struct Array *, float, float, float);
extern void __fastcall remove_range_029170(struct Array *, float, float, float);
extern void __fastcall remove_range_029260(struct Array *, float, float, float);
extern void __fastcall remove_range_029a90(struct Array *, float, float, float);
extern void __fastcall remove_range_066210(struct Array *, float, float, float);
extern void __fastcall remove_range_077960(struct Array *, float, float, float);
extern void __fastcall remove_range_07d430(struct Array *, float, float, float);
extern void __fastcall remove_range_0802d0(struct Array *, float, float, float);
extern void __fastcall remove_range_09df90(struct Array *, float, float, float);
extern void __fastcall remove_range_0da200(struct Array *, float, float, float);
extern void __fastcall remove_range_0da320(struct Array *, float, float, float);
extern void __fastcall remove_range_1a9440(struct Array *, float, float, float);
extern void __fastcall remove_range_1a94d0(struct Array *, float, float, float);
extern void __fastcall remove_range_2db4e0(struct Array *, float, float, float);
extern void __fastcall remove_range_2db690(struct Array *, float, float, float);
extern void __fastcall remove_range_2e5ed0(struct Array *, float, float, float);

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
        array->capacity = 0; \
        array->count = 0; \
        if (flags.value & 1) free_allocation(self); \
        return self; \
    }

DELETE_ARRAY_MEMBER(delete_array_member_073cb0, 16, 12, remove_range_029260)
DELETE_ARRAY_MEMBER(delete_array_member_07dcd0, 4, 16, remove_range_07d430)
DELETE_ARRAY_MEMBER(delete_array_member_082950, 4, 12, remove_range_029260)
DELETE_ARRAY_MEMBER(delete_array_member_082a00, 20, 2, remove_range_007710)
DELETE_ARRAY_MEMBER(delete_array_member_083ca0, 20, 28, remove_range_0802d0)
DELETE_ARRAY_MEMBER(delete_array_member_092480, 8, 4, remove_range_029170)
DELETE_ARRAY_MEMBER(delete_array_member_09f530, 12, 4, remove_range_09df90)
DELETE_ARRAY_MEMBER(delete_array_member_0c0120, 4, 1, remove_range_066210)
DELETE_ARRAY_MEMBER(delete_array_member_0c2210, 20, 4, remove_range_077960)
DELETE_ARRAY_MEMBER(delete_array_member_0eb9a0, 24, 2, remove_range_007710)
DELETE_ARRAY_MEMBER(delete_array_member_0eba80, 12, 16, remove_range_0da200)
DELETE_ARRAY_MEMBER(delete_array_member_0f35b0, 12, 8, remove_range_0da320)
DELETE_ARRAY_MEMBER(delete_array_member_15f770, 4, 4, remove_range_029170)
DELETE_ARRAY_MEMBER(delete_array_member_16f720, 4, 1, remove_range_066210)
DELETE_ARRAY_MEMBER(delete_array_member_170230, 24, 2, remove_range_007710)
DELETE_ARRAY_MEMBER(delete_array_member_170290, 24, 2, remove_range_007710)
DELETE_ARRAY_MEMBER(delete_array_member_1aa610, 4, 4, remove_range_1a9440)
DELETE_ARRAY_MEMBER(delete_array_member_1aa670, 8, 4, remove_range_1a94d0)
DELETE_ARRAY_MEMBER(delete_array_member_25e910, 8, 4, remove_range_029170)
DELETE_ARRAY_MEMBER(delete_array_member_2dd020, 148, 4, remove_range_029170)
DELETE_ARRAY_MEMBER(delete_array_member_2e2990, 4, 20, remove_range_2db690)
DELETE_ARRAY_MEMBER(delete_array_member_2e8b90, 12, 4, remove_range_029a90)
DELETE_ARRAY_MEMBER(delete_array_member_315d40, 12, 4, remove_range_2db4e0)
DELETE_ARRAY_MEMBER(delete_array_member_325df0, 4, 4, remove_range_029170)
DELETE_ARRAY_MEMBER(delete_array_member_34da60, 20, 4, remove_range_2e5ed0)
