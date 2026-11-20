typedef unsigned short WChar;
struct Class;
struct Field {
    unsigned char reserved[0x2c];
    struct Field *next;
};
struct Class {
    unsigned char reserved[0x18];
    struct Class *outer;
    unsigned flags;
    unsigned name_index;
    struct Class *type;
    struct Class *super;
    struct Field *next;
    struct Field *children;
};
struct FieldRef { struct Field *ptr; };
struct NameEntry { unsigned header[3]; WChar text[1]; };

extern struct Class target_type;
extern struct NameEntry **names;
extern const WChar actor_name[], actor_prefix[], object_prefix[], name_format[];
extern WChar class_name_buffer[];
__declspec(dllimport) int __cdecl _wcsicmp(const WChar *, const WChar *);
extern int __cdecl format_name(WChar *, const WChar *, ...);

void __fastcall prepend_child(struct Class *owner, struct FieldRef child)
{
    child.ptr->next = owner->children;
    owner->children = child.ptr;
}

struct Class *__fastcall find_typed_outer(struct Class *object)
{
    while (object->type != &target_type)
        object = object->outer;
    return object;
}

const WChar *__fastcall get_cpp_name(struct Class *self)
{
    struct Class *ancestor;
    const WChar *prefix;
    for (ancestor = self; ancestor; ancestor = ancestor->super) {
        const WChar *text = names[ancestor->name_index]->text;
        if (_wcsicmp(text, actor_name) == 0)
            break;
    }
    prefix = ancestor ? actor_prefix : object_prefix;
    format_name(class_name_buffer, name_format, prefix, names[self->name_index]->text);
    return class_name_buffer;
}
