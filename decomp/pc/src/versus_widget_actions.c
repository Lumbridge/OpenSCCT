struct Widget;
struct Argument { unsigned value; };
struct WidgetRef { struct Widget *widget; };
struct Handler;
struct Service;
typedef void (__fastcall *Notify)(struct Handler *, struct WidgetRef, struct Argument);
typedef void (__fastcall *SetMode)(struct Service *, float);
struct Handler { Notify *methods; };
struct Service { SetMode *methods; };
extern struct Service *event_service;
struct Widget {
    unsigned prefix[11];
    struct Handler *handler;
    unsigned char padding[164];
    unsigned char flags;
};
extern unsigned char __fastcall visit_2(struct Widget *);
extern unsigned char __fastcall visit_3(struct Widget *);
extern unsigned char __fastcall visit_4(struct Widget *);
extern unsigned char __fastcall visit_5(struct Widget *);

/* A float fastcall argument carries the mode in a four-byte stack slot. */
static __forceinline void set_mode(float mode_bits)
{
    if (event_service)
        event_service->methods[2](event_service, mode_bits);
}

unsigned char __fastcall begin_event(struct Widget *widget, struct Argument argument)
{
    if (widget->flags & 2) {
        struct WidgetRef ref;
        widget->flags |= 8;
        ref.widget = widget;
        widget->handler->methods[16](widget->handler, ref, argument);
        set_mode(0.0f);
        return visit_2(widget);
    }
    return 0;
}

unsigned char __fastcall end_event(struct Widget *widget)
{
    struct WidgetRef ref;
    typedef void (__fastcall *NotifyEnd)(struct Handler *, struct WidgetRef, float);
    widget->flags &= ~8u;
    ref.widget = widget;
    ((NotifyEnd)widget->handler->methods[16])(widget->handler, ref, 0.0f);
    return visit_3(widget);
}

#define MODE_EVENT(Entry, ModeBits, Visit) \
    unsigned char __fastcall Entry(struct Widget *widget) \
    { \
        if (widget->flags & 2) { \
            set_mode(ModeBits); \
            return Visit(widget); \
        } \
        return 0; \
    }

MODE_EVENT(first_mode_event, 1.40129846e-45f, visit_4)
MODE_EVENT(second_mode_event, 2.80259693e-45f, visit_5)
