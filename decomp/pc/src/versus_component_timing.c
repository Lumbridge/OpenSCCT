struct Component;
struct Handler;
struct Word { unsigned value; };
typedef void (__fastcall *FlagMethod)(struct Handler *, struct Word);
struct Handler { FlagMethod *methods; };
struct Child { unsigned reserved; unsigned char enabled, padding[39]; struct Handler *handler; };
struct Component {
    unsigned prefix[10];
    struct Child **first, **last;
    unsigned reserved;
    float time;
    int interval;
    unsigned char flag, enabled, padding[6];
    int *steps;
};
extern void __fastcall run_child(struct Child *);

void __fastcall set_flag(struct Component *component, struct Word value)
{
    struct Child **it;
    if (component->flag != (unsigned char)value.value) {
        component->flag = (unsigned char)value.value;
        for (it = component->first; it != component->last; ++it) {
            struct Handler *handler = (*it)->handler;
            handler->methods[5](handler, value);
        }
    }
}


void __fastcall set_enabled(struct Component *component, unsigned unused, unsigned char value, unsigned char propagate)
{
    struct Child **it;
    component->enabled = value;
    if (propagate) {
        for (it = component->first; it != component->last; ++it)
            (*it)->enabled = value;
    }
}

void __fastcall run_children(struct Component *component)
{
    struct Child **it;
    if (component->enabled) {
        for (it = component->first; it != component->last; ++it)
            run_child(*it);
    }
}
