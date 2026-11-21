struct Event { unsigned char prefix[20]; unsigned id; };
struct View {
    unsigned char prefix[48];
    struct Event *source;
    unsigned first, second;
    unsigned char active;
};

unsigned char __fastcall clear_active(struct View *view)
{
    if (view->active) {
        view->first = 0;
        view->second = 0;
    }
    return 1;
}

struct Control { unsigned char prefix[24]; unsigned char flags; };
struct Component {
    unsigned char prefix[8];
    struct Control *control;
    unsigned reserved;
    void *target;
    unsigned reserved2;
    unsigned char active;
};
/* The target reads its value from the stack and overwrites EDX. */
extern void __fastcall set_target(void *target, unsigned unused_edx, unsigned value);
void __fastcall dismiss(struct Component *component, unsigned unused)
{
    if (component->active) {
        component->control->flags &= ~1u;
        set_target(component->target, unused, 0);
    }
}
