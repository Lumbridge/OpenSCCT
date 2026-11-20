struct Widget;
struct Listener;
struct OwnerArgument { struct Widget *widget; };
struct EventArgument { void *data; };
typedef unsigned char (__fastcall *Event)(struct Listener *, struct OwnerArgument);
typedef unsigned char (__fastcall *ValueEvent)(struct Listener *, struct OwnerArgument, struct EventArgument);
struct Listener { void **methods; unsigned reserved; unsigned char enabled; };
struct Widget { unsigned prefix[55]; struct Listener **first, **last; };

#define EVENT(Slot) \
    unsigned char __fastcall dispatch_##Slot(struct Widget *widget) \
    { \
        struct Listener **position = widget->last; \
        struct OwnerArgument context; \
        context.widget = widget; \
        while (position != widget->first) { \
            struct Listener *listener = position[-1]; \
            if (listener->enabled && ((Event)listener->methods[Slot])(listener, context)) \
                return 1; \
            --position; \
        } \
        return 0; \
    }

#define VALUE_EVENT(Slot) \
    unsigned char __fastcall dispatch_##Slot(struct Widget *widget, struct EventArgument event) \
    { \
        struct Listener **position = widget->last; \
        struct OwnerArgument context; \
        context.widget = widget; \
        while (position != widget->first) { \
            struct Listener *listener = position[-1]; \
            if (listener->enabled && ((ValueEvent)listener->methods[Slot])(listener, context, event)) \
                return 1; \
            --position; \
        } \
        return 0; \
    }

EVENT(2)
EVENT(3)
EVENT(4)
EVENT(5)
VALUE_EVENT(6)
VALUE_EVENT(7)
VALUE_EVENT(8)
VALUE_EVENT(9)
VALUE_EVENT(10)
VALUE_EVENT(11)
EVENT(12)
EVENT(13)
