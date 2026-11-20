struct Widget;
struct Key { unsigned code; };
struct KeyArgument { const struct Key *key; };
typedef void (__fastcall *Action)(struct Widget *);
struct Widget { Action *methods; };
extern unsigned char __fastcall dispatch_key(struct Widget *, struct KeyArgument);

unsigned char __stdcall handle_key(struct Widget *widget, struct KeyArgument event)
{
    unsigned char handled = dispatch_key(widget, event);
    switch (event.key->code) {
    case 13:
    case 128:
        widget->methods[5](widget);
        return 1;
    case 27:
    case 129:
        widget->methods[6](widget);
        return 1;
    default:
        return handled;
    }
}
