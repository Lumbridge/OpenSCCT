struct RuntimeClass;
struct Object;
extern struct Object *find_object(struct RuntimeClass *, struct Object *, const unsigned short *, int);

__declspec(noinline) struct Object *forward_find_object(struct RuntimeClass *type, struct Object *outer,
                                                      const unsigned short *name, int exact_class)
{
    return find_object(type, outer, name, exact_class);
}

#define DEFINE_CLASS_LOOKUP(Type, Package) \
    extern struct RuntimeClass *class_##Type; \
    extern struct RuntimeClass *create_##Type(const unsigned short *); \
    extern void register_##Type(void); \
    struct Object *forward_find_##Type(struct Object *outer, const unsigned short *name, int exact_class) \
    { \
        if (!class_##Type) { \
            class_##Type = create_##Type(package_##Package); \
            register_##Type(); \
        } \
        return forward_find_object(class_##Type, outer, name, exact_class); \
    }

extern const unsigned short package_Core[];
DEFINE_CLASS_LOOKUP(UClass, Core)
DEFINE_CLASS_LOOKUP(UEnum, Core)
