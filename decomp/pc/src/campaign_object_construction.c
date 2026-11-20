struct Object;
struct Class {
    unsigned char prefix[0x2a4];
    void (__cdecl *construct)(struct Object *object);
};

extern struct Object *__cdecl default_outer(void);
extern void *allocation_context;
extern struct Object *__cdecl allocate_object(struct Class *, struct Object *, unsigned,
                                               unsigned, const struct Object *, void *,
                                               void *, void *);
struct Object *__cdecl construct_object(struct Class *type, struct Object *outer, unsigned name,
                                        unsigned flags, const struct Object *model,
                                        void *context, void *instance)
{
    struct Object *object = allocate_object(type, outer, name, flags, model, context, 0, instance);
    if (object)
        type->construct(object);
    return object;
}

struct Object *__cdecl construct_default(struct Class *type, struct Object *outer,
                                         unsigned name, unsigned flags)
{
    if (outer == (struct Object *)-1)
        outer = default_outer();
    return construct_object(type, outer, name, flags, 0, allocation_context, 0);
}
