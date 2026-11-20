struct Descriptor;
struct Fields {
    const char *name;
    const struct Descriptor *parent;
    const void *cinfo_functions;
    unsigned size_cinfo;
    const void *enums;
    unsigned enum_count;
    const void *members;
    unsigned member_count;
};
struct Descriptor {
    const void *methods;
    unsigned short allocation_size;
    unsigned short references;
    struct Fields fields;
};
extern const void *const descriptor_methods[];
struct Descriptor *__fastcall construct(struct Descriptor *self, struct Fields args)
{
    self->references = 1;
    self->methods = descriptor_methods;
    self->fields.name = args.name;
    self->fields.parent = args.parent;
    self->fields.cinfo_functions = args.cinfo_functions;
    self->fields.size_cinfo = args.size_cinfo;
    self->fields.enums = args.enums;
    self->fields.enum_count = args.enum_count;
    self->fields.members = args.members;
    self->fields.member_count = args.member_count;
    return self;
}
