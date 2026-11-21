struct Point { short x, y; };
struct Bounds { short left, right, top, bottom; };
struct Entry { unsigned char data[32]; };
struct Widget {
    unsigned char prefix[96];
    int axis;
    unsigned char reserved[4];
    struct Entry *entries, *entries_end;
    unsigned char reserved2[8];
    unsigned *items, *items_end;
    unsigned char reserved3[44];
    unsigned selection;
};
void __fastcall advance_position(struct Widget *widget, unsigned unused, struct Point *point, const struct Bounds *bounds)
{
    switch (widget->axis) {
    case 0: point->y += bounds->bottom - bounds->top; break;
    case 1: point->x += bounds->right - bounds->left; break;
    }
}
static unsigned entry_count(struct Widget *widget)
{
    return widget->entries ? widget->entries_end - widget->entries : 0;
}
static unsigned item_count(struct Widget *widget)
{
    return widget->items ? widget->items_end - widget->items : 0;
}
unsigned char __fastcall previous_item(struct Widget *widget)
{
    if (!entry_count(widget) || !item_count(widget) || !widget->selection) return 0;
    --widget->selection;
    return 1;
}
