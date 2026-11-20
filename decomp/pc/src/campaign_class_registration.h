struct RuntimeClass;
struct ClassMethods {
    void *prefix[0x50 / 4];
    void (__fastcall *register_class)(struct RuntimeClass *);
};
struct RuntimeClass {
    const struct ClassMethods *methods;
    unsigned char object_fields[0x24 - 4];
    struct RuntimeClass *object_class;
    struct RuntimeClass *super_class;
    unsigned char class_fields[0x28c - 0x2c];
    struct RuntimeClass *within;
};

extern int object_system_initialized(void);

#define CLASS_GETTER(Type, Package) \
    extern struct RuntimeClass *class_##Type; \
    extern struct RuntimeClass *create_##Type(const unsigned short *); \
    extern void register_##Type(void); \
    static __forceinline struct RuntimeClass *get_##Type(void) \
    { \
        if (!class_##Type) { \
            class_##Type = create_##Type(package_##Package); \
            register_##Type(); \
        } \
        return class_##Type; \
    }

#define REGISTER_RUNTIME_CLASS(Type, Parent, Within) \
    extern struct RuntimeClass *class_##Type; \
    void register_##Type(void) \
    { \
        struct RuntimeClass *parent = get_##Parent(); \
        if (parent != class_##Type) \
            class_##Type->super_class = get_##Parent(); \
        else \
            class_##Type->super_class = 0; \
        class_##Type->within = get_##Within(); \
        class_##Type->object_class = get_UClass(); \
        if (object_system_initialized()) { \
            struct RuntimeClass *actual = class_##Type->object_class; \
            if (actual == get_UClass()) \
                class_##Type->methods->register_class(class_##Type); \
        } \
    }
