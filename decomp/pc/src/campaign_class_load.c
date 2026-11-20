struct RuntimeClass;
struct Object;
extern struct Object *load_object(struct RuntimeClass *, struct Object *, const unsigned short *,
                                  const unsigned short *, unsigned, void *);

#define DEFINE_CLASS_LOAD(Type, Package) \
    extern struct RuntimeClass *class_##Type; \
    extern struct RuntimeClass *create_##Type(const unsigned short *); \
    extern void register_##Type(void); \
    struct Object *load_##Type(struct Object *outer, const unsigned short *name, const unsigned short *file, unsigned flags, void *sandbox) \
    { \
        if (!class_##Type) { \
            class_##Type = create_##Type(package_##Package); \
            register_##Type(); \
        } \
        return load_object(class_##Type, outer, name, file, flags, sandbox); \
    }

extern const unsigned short package_Core[];
extern const unsigned short package_Engine[];
DEFINE_CLASS_LOAD(UClass, Core)
DEFINE_CLASS_LOAD(ULevel, Engine)
