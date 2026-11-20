struct Child;
struct Listener;
struct Service;
struct Handler;
struct Word { unsigned value; };
struct ChildArgument { struct Child *child; };
typedef unsigned char (__fastcall *Event)(struct Listener *, struct ChildArgument);
typedef void (__fastcall *Run)(struct Handler *);
typedef void (__fastcall *TimeEvent)(struct Handler *, float);
struct Listener { Event *methods; unsigned reserved; unsigned char enabled; };
struct Handler { void **methods; };
struct Child {
    unsigned reserved;
    unsigned char enabled, suspended, padding[34];
    int index;
    struct Handler *handler;
    unsigned opaque[5];
    struct Listener **first, **last;
    unsigned other;
    struct Word service_value;
};
extern struct Service *ui_service;
extern void __fastcall prepare_child(struct Service *, struct Word);
extern void __fastcall evaluate_time(struct Child *, float, struct Word, struct Word);

static __forceinline void visit(struct Child *child, unsigned slot)
{
    struct Listener **it = child->last;
    struct ChildArgument argument;
    argument.child = child;
    while (it != child->first) {
        struct Listener *listener = it[-1];
        if (listener->enabled && listener->methods[slot](listener, argument))
            break;
        --it;
    }
}

void __fastcall run_child(struct Child *child)
{
    if (child->suspended || !child->enabled || child->index == -1)
        return;
    prepare_child(ui_service, child->service_value);
    visit(child, 1);
    ((Run)child->handler->methods[2])(child->handler);
    visit(child, 2);
}

void __fastcall update_child(struct Child *child, float time, struct Word interval, struct Word mode)
{
    evaluate_time(child, time, interval, mode);
    ((TimeEvent)child->handler->methods[7])(child->handler, time);
}
