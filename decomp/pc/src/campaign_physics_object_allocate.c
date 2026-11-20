struct Object { void *methods; unsigned short allocation_size; };
struct Manager;
struct Size { unsigned value; };
union StackWord { unsigned value; float argument; };
struct Methods {
    void *reserved[4];
    struct Object *(__fastcall *allocate)(struct Manager *, struct Size, float);
};
struct Manager { const struct Methods *methods; };
extern struct Manager *manager;

#define ALLOCATE_OBJECT(Entry, Category) \
    struct Object *__cdecl Entry(unsigned bytes) \
    { \
        struct Size size; \
        static const union StackWord category = {Category}; \
        struct Object *object; \
        size.value = bytes; \
        object = manager->methods->allocate(manager, size, category.argument); \
        object->allocation_size = (unsigned short)bytes; \
        return object; \
    }

ALLOCATE_OBJECT(allocate_08a550, 34)
ALLOCATE_OBJECT(allocate_08a5f0, 34)
ALLOCATE_OBJECT(allocate_08a700, 40)
ALLOCATE_OBJECT(allocate_08a9a0, 6)
ALLOCATE_OBJECT(allocate_0a0040, 39)
ALLOCATE_OBJECT(allocate_0a1e30, 39)
ALLOCATE_OBJECT(allocate_0a3ab0, 36)
ALLOCATE_OBJECT(allocate_177e70, 19)
ALLOCATE_OBJECT(allocate_1b7710, 21)
ALLOCATE_OBJECT(allocate_1b7730, 5)
ALLOCATE_OBJECT(allocate_1b7790, 5)
ALLOCATE_OBJECT(allocate_4fcd40, 19)
ALLOCATE_OBJECT(allocate_4fcda0, 19)
ALLOCATE_OBJECT(allocate_4fce00, 19)
ALLOCATE_OBJECT(allocate_4fe9f0, 19)
ALLOCATE_OBJECT(allocate_500f50, 21)
ALLOCATE_OBJECT(allocate_501c80, 21)
ALLOCATE_OBJECT(allocate_502c80, 25)
ALLOCATE_OBJECT(allocate_503880, 5)
ALLOCATE_OBJECT(allocate_5038a0, 6)
ALLOCATE_OBJECT(allocate_504550, 6)
ALLOCATE_OBJECT(allocate_5129a0, 26)
ALLOCATE_OBJECT(allocate_517510, 26)
ALLOCATE_OBJECT(allocate_5507b0, 34)
ALLOCATE_OBJECT(allocate_5508a0, 29)
ALLOCATE_OBJECT(allocate_5508c0, 4)
ALLOCATE_OBJECT(allocate_550a20, 4)
ALLOCATE_OBJECT(allocate_550a40, 45)
ALLOCATE_OBJECT(allocate_550aa0, 4)
ALLOCATE_OBJECT(allocate_567170, 29)
ALLOCATE_OBJECT(allocate_577380, 12)
ALLOCATE_OBJECT(allocate_5775c0, 12)
ALLOCATE_OBJECT(allocate_578790, 36)
ALLOCATE_OBJECT(allocate_578980, 11)
ALLOCATE_OBJECT(allocate_578df0, 11)
ALLOCATE_OBJECT(allocate_579330, 5)
ALLOCATE_OBJECT(allocate_579960, 19)
ALLOCATE_OBJECT(allocate_57c690, 5)
ALLOCATE_OBJECT(allocate_57ecc0, 19)
ALLOCATE_OBJECT(allocate_57ef60, 5)
ALLOCATE_OBJECT(allocate_57f950, 5)
ALLOCATE_OBJECT(allocate_57faf0, 5)
ALLOCATE_OBJECT(allocate_580fb0, 5)
ALLOCATE_OBJECT(allocate_582f00, 5)
ALLOCATE_OBJECT(allocate_582f20, 5)
ALLOCATE_OBJECT(allocate_582f40, 5)
ALLOCATE_OBJECT(allocate_582f60, 5)
ALLOCATE_OBJECT(allocate_582f80, 5)
ALLOCATE_OBJECT(allocate_5830f0, 5)
ALLOCATE_OBJECT(allocate_584270, 5)
ALLOCATE_OBJECT(allocate_5868d0, 5)
ALLOCATE_OBJECT(allocate_588380, 5)
ALLOCATE_OBJECT(allocate_5883c0, 5)
ALLOCATE_OBJECT(allocate_5883e0, 5)
ALLOCATE_OBJECT(allocate_588400, 5)
ALLOCATE_OBJECT(allocate_588420, 5)
ALLOCATE_OBJECT(allocate_588440, 5)
ALLOCATE_OBJECT(allocate_588460, 5)
ALLOCATE_OBJECT(allocate_5890b0, 5)
ALLOCATE_OBJECT(allocate_589170, 5)
ALLOCATE_OBJECT(allocate_589220, 5)
ALLOCATE_OBJECT(allocate_599590, 10)
ALLOCATE_OBJECT(allocate_5996e0, 10)
ALLOCATE_OBJECT(allocate_5997e0, 10)
ALLOCATE_OBJECT(allocate_59a3e0, 10)
ALLOCATE_OBJECT(allocate_59a600, 10)
ALLOCATE_OBJECT(allocate_59c610, 44)
ALLOCATE_OBJECT(allocate_6ee850, 19)
ALLOCATE_OBJECT(allocate_6efed0, 6)
ALLOCATE_OBJECT(allocate_6f28e0, 28)
ALLOCATE_OBJECT(allocate_718aa0, 39)
ALLOCATE_OBJECT(allocate_791560, 1)
