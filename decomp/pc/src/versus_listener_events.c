struct Owner;
struct Listener;
struct OwnerArgument { struct Owner *owner; };
struct Word { unsigned value; };
typedef unsigned char (__fastcall *Event)(struct Listener *, struct OwnerArgument);
typedef unsigned char (__fastcall *ValueEvent)(struct Listener *, struct OwnerArgument, struct Word);
struct Listener {
    void **methods;
    unsigned reserved;
    unsigned char enabled;
};
struct Owner {
    unsigned prefix[21];
    struct Listener **first;
    struct Listener **last;
};

void __fastcall visit_1(struct Owner *owner)
{
    struct Listener **position;
    struct OwnerArgument context;
    context.owner = owner;
    position = owner->last;
    while (position != owner->first) {
        struct Listener *listener = position[-1];
        if (listener->enabled && ((Event)listener->methods[1])(listener, context))
            break;
        --position;
    }
}

void __fastcall visit_3(struct Owner *owner, unsigned unused, struct Word argument)
{
    struct Listener **position;
    struct OwnerArgument context;
    context.owner = owner;
    position = owner->last;
    while (position != owner->first) {
        struct Listener *listener = position[-1];
        if (listener->enabled && ((ValueEvent)listener->methods[3])(listener, context, argument))
            break;
        --position;
    }
}

void __fastcall visit_4(struct Owner *owner, unsigned unused, struct Word argument)
{
    struct Listener **position;
    struct OwnerArgument context;
    context.owner = owner;
    position = owner->last;
    while (position != owner->first) {
        struct Listener *listener = position[-1];
        if (listener->enabled && ((ValueEvent)listener->methods[4])(listener, context, argument))
            break;
        --position;
    }
}

struct Rectangle { short left, right, top, bottom; };
struct Point { short x, y; };

int __fastcall contains_point(const struct Rectangle *rectangle, unsigned unused, const struct Point *point)
{
    if (point->x < rectangle->left || point->x > rectangle->right ||
        point->y < rectangle->top || point->y > rectangle->bottom)
        return 0;
    return 1;
}
