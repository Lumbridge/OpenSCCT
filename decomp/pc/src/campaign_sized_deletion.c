struct Object;
struct Allocation { struct Object *object; };
struct Flags { unsigned value; };
struct Manager;
struct Methods {
    void *reserved[5];
    void (__fastcall *release)(struct Manager *, struct Allocation, unsigned __int64);
};
struct Manager { const struct Methods *methods; };
extern struct Manager *manager;
extern void __fastcall destroy(struct Object *);

#define DELETE_SIZED(Bytes, Category) \
struct Object *__fastcall delete_##Bytes##_##Category(struct Object *self, struct Flags flags) \
{ \
    destroy(self); \
    if (flags.value & 1) { \
        struct Allocation allocation; \
        allocation.object = self; \
        manager->methods->release(manager, allocation, ((unsigned __int64)Category << 32) | Bytes); \
    } \
    return self; \
}

DELETE_SIZED(12, 23)
DELETE_SIZED(112, 34)
DELETE_SIZED(260, 34)
