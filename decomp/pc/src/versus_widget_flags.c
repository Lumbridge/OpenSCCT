struct Widget;
struct WidgetRef { struct Widget *widget; };
struct Handler;
typedef void (__fastcall *Notify)(struct Handler *, struct WidgetRef, float);
struct Handler { Notify *methods; };
struct Widget {
    unsigned prefix[11];
    struct Handler *handler;
    unsigned char padding[164];
    unsigned char bit0 : 1, enabled : 1, bit2 : 1, bit3 : 1, bit4 : 1;
};

#define SET_FLAG(Name) \
    void __fastcall set_##Name(struct Widget *widget, unsigned unused, unsigned char value) \
    { \
        if (widget->Name != value && widget->enabled) { \
            struct WidgetRef ref; \
            widget->Name = value; \
            ref.widget = widget; \
            widget->handler->methods[16](widget->handler, ref, 0.0f); \
        } \
    }
SET_FLAG(bit2)
SET_FLAG(bit4)
