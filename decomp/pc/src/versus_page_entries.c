struct Entry { unsigned char data[32]; };
struct Page {
    unsigned char prefix[84];
    int selection;
    unsigned char gap[12];
    unsigned allocator;
    struct Entry *first, *end;
};
/* The retail member call takes its receiver in ECX and two stack arguments.
   VC7.1 C uses fastcall here to describe that layout. The callee overwrites EDX
   before reading it, so the bridge passes the first-entry pointer already there. */
extern void __fastcall select_entry(struct Page *, void *unused_edx, int index, int flag);

static unsigned count(struct Page *page)
{
    return page->first ? page->end - page->first : 0;
}
unsigned char __fastcall previous_entry(struct Page *page)
{
    if (!count(page) || !page->selection) return 0;
    select_entry(page, page->first, page->selection - 1, 0);
    return 1;
}
unsigned char __fastcall first_entry(struct Page *page)
{
    if (!count(page) || !page->selection) return 0;
    select_entry(page, page->first, 0, 0);
    return 1;
}

struct Pair { unsigned first, second; };
struct Pair *__cdecl copy_pairs(const struct Pair *first, const struct Pair *end, struct Pair *output)
{
    while (first != end) {
        *output = *first;
        ++first;
        ++output;
    }
    return output;
}

struct Point { short x, y; };
struct Bounds { short left, right, top, bottom; };
struct Widget {
    unsigned char prefix[28];
    void *component;
    unsigned char reserved[112];
    struct Bounds bounds;
    unsigned char gap[8];
    int visible_count, first_visible;
};
struct BoundsRef { struct Bounds *bounds; };
extern void __fastcall measure(void *, struct BoundsRef);
/* EDX contains x at the retail call site. The positioning routine reads
   the two stack coordinates and overwrites EDX before using it. */
extern void __fastcall move_bounds(struct Bounds *, int unused_edx, int x, int y);
static int contains(const struct Bounds *bounds, const struct Point *point)
{
    return point->x >= bounds->left && point->x <= bounds->right &&
           point->y >= bounds->top && point->y <= bounds->bottom;
}
int __fastcall hit_entry(struct Widget *widget, unsigned unused, const struct Point *point)
{
    int i;
    if (widget->component && contains(&widget->bounds, point)) {
        struct Bounds rect;
        struct BoundsRef ref;
        int x, y;
        rect.left = 0; rect.right = 0; rect.top = 0; rect.bottom = 0;
        ref.bounds = &rect;
        measure(widget->component, ref);
        x = widget->bounds.left;
        y = widget->bounds.top;
        move_bounds(&rect, x, x, y);
        for (i = 0; i < widget->visible_count; ++i) {
            int height;
            if (contains(&rect, point)) return widget->first_visible + i;
            height = rect.bottom - rect.top;
            rect.top += height;
            rect.bottom += height;
        }
    }
    return -1;
}
