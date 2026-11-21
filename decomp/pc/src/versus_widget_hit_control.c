struct PointArg { const struct Point *point; };
struct Point { short x, y; };
struct Bounds { short left, right, top, bottom; };
struct Entry { unsigned char data[32]; };
struct Widget {
    unsigned char prefix[48];
    unsigned previous;
    unsigned char reserved[16];
    unsigned next;
    unsigned char reserved2[12];
    unsigned selected;
    unsigned char reserved3[16];
    struct Entry *first, *end;
    unsigned char reserved4[24];
    struct Bounds previous_bounds;
    unsigned char reserved5[8];
    struct Bounds next_bounds;
};
static int contains(const struct Bounds *bounds, const struct Point *point)
{
    return point->x >= bounds->left && point->x <= bounds->right &&
           point->y >= bounds->top && point->y <= bounds->bottom;
}
extern int __fastcall hit_entry(struct Widget *, struct PointArg);
int __fastcall hit_control(struct Widget *widget, unsigned unused, const struct Point *point)
{
    struct PointArg arg;
    if (widget->previous && contains(&widget->previous_bounds, point) && widget->selected)
        return -2;
    if (widget->next && contains(&widget->next_bounds, point)) {
        unsigned count;
        if (!widget->first)
            count = 0;
        else
            count = widget->end - widget->first;
        if (widget->selected != count - 1) return -3;
    }
    arg.point = point;
    return hit_entry(widget, arg);
}
