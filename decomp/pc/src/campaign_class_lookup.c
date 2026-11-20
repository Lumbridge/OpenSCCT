struct RuntimeClass;
struct Object;
extern struct Object *find_object(struct RuntimeClass *, struct Object *, const unsigned short *, int);

#define DEFINE_CLASS_LOOKUP(Type, Package) \
    extern struct RuntimeClass *class_##Type; \
    extern struct RuntimeClass *create_##Type(const unsigned short *); \
    extern void register_##Type(void); \
    struct Object *find_##Type(struct Object *outer, const unsigned short *name, int exact_class) \
    { \
        if (!class_##Type) { \
            class_##Type = create_##Type(package_##Package); \
            register_##Type(); \
        } \
        return find_object(class_##Type, outer, name, exact_class); \
    }

extern const unsigned short package_Core[];
extern const unsigned short package_Engine[];
DEFINE_CLASS_LOOKUP(AActor, Engine)
DEFINE_CLASS_LOOKUP(UClass, Core)
DEFINE_CLASS_LOOKUP(UObject, Core)
DEFINE_CLASS_LOOKUP(UPackage, Core)
DEFINE_CLASS_LOOKUP(UShader, Engine)
DEFINE_CLASS_LOOKUP(UStaticMesh, Engine)
DEFINE_CLASS_LOOKUP(UTexEnvMap, Engine)
DEFINE_CLASS_LOOKUP(UTexture, Engine)
