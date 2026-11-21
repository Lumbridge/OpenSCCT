struct Whole;
struct Flags { unsigned value; };
struct Allocation { struct Whole *object; };
struct Allocator;
struct Methods { void *reserved[2]; void (__fastcall *release)(struct Allocator *, struct Allocation); };
struct Allocator { const struct Methods *methods; };
extern struct Allocator *allocator;
extern void __fastcall destruct(unsigned char *);
extern void __fastcall destruct_base(unsigned char *);

#define DELETE_PART(OFFSET) \
struct Whole *__fastcall delete_part_##OFFSET(unsigned char *part, struct Flags flags) \
{ \
    struct Whole *object = (struct Whole *)(part - OFFSET); \
    unsigned char *receiver = (unsigned char *)object + OFFSET; \
    destruct(receiver); \
    destruct_base(receiver); \
    if (flags.value & 1) { \
        struct Allocation allocation; \
        allocation.object = object; \
        allocator->methods->release(allocator, allocation); \
    } \
    return object; \
}

DELETE_PART(32)
DELETE_PART(40)
DELETE_PART(48)
DELETE_PART(56)
DELETE_PART(60)
DELETE_PART(68)
DELETE_PART(88)
DELETE_PART(112)
DELETE_PART(120)
DELETE_PART(124)
DELETE_PART(224)
DELETE_PART(2384)
