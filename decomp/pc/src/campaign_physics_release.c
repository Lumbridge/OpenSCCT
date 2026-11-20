struct Object { const void *methods; };
struct Flags { unsigned value; };
struct ObjectArg { struct Object *object; };
struct Manager;
struct ManagerMethods {
    void *reserved[5];
    void (__fastcall *release)(struct Manager *, struct ObjectArg, unsigned __int64);
};
struct Manager { const struct ManagerMethods *methods; };
extern struct Manager *manager;
extern const void *const methods_84aa68[];
extern const void *const methods_84aa70[];
extern const void *const methods_84bc90[];

#define RELEASE_OBJECT(Entry, BaseMethods, Bytes, Category) \
    struct Object *__fastcall Entry(struct Object *self, struct Flags flags) \
    { \
        self->methods = BaseMethods; \
        if (flags.value & 1) { \
            struct ObjectArg arg; \
            arg.object = self; \
            manager->methods->release(manager, arg, ((unsigned __int64)Category << 32) | Bytes); \
        } \
        return self; \
    }

RELEASE_OBJECT(release_512e60, methods_84aa68, 8, 26)
RELEASE_OBJECT(release_512e90, methods_84aa70, 8, 26)
RELEASE_OBJECT(release_512ec0, methods_84aa68, 8, 26)
RELEASE_OBJECT(release_512f30, methods_84aa68, 8, 26)
RELEASE_OBJECT(release_512f60, methods_84aa70, 8, 26)
RELEASE_OBJECT(release_53ae50, methods_84aa70, 8, 26)
RELEASE_OBJECT(release_53ae80, methods_84aa68, 8, 26)
RELEASE_OBJECT(release_5509f0, methods_84bc90, 4, 28)
RELEASE_OBJECT(release_552ba0, methods_84bc90, 4, 28)
RELEASE_OBJECT(release_6f1a70, methods_84bc90, 4, 28)
