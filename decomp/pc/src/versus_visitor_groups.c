struct Visitor;
struct Element;
struct VisitorRef { struct Visitor *visitor; };
typedef void (__fastcall *Accept)(struct Element *, struct VisitorRef);
struct Element { Accept *methods; };
struct Group { unsigned prefix[2]; struct Element **first, **last; };
struct Owner { unsigned prefix[10]; struct Element **first, **last; };

void __fastcall visit_group(struct Visitor *visitor, unsigned unused, struct Group *group)
{
    struct Element **it;
    struct VisitorRef arg;
    arg.visitor = visitor;
    for (it = group->first; it != group->last; ++it)
        (*it)->methods[1](*it, arg);
}

static __forceinline int count(const struct Owner *owner)
{
    if (!owner->first) return 0;
    return owner->last - owner->first;
}

void __fastcall visit_owner(struct Visitor *visitor, unsigned unused, struct Owner *owner)
{
    int i;
    struct VisitorRef arg;
    arg.visitor = visitor;
    for (i = 0; i < count(owner); ++i) {
        struct Element *element = ((volatile struct Owner *)owner)->first[i];
        element->methods[1](element, arg);
    }
}
