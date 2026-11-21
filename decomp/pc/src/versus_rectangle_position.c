struct Rectangle { short left, right, top, bottom; };
struct Point { short x, y; };

void __fastcall move_point(struct Rectangle *rect, unsigned unused, const struct Point *point)
{
    int width = rect->right - rect->left;
    int height = rect->bottom - rect->top;
    rect->left = point->x;
    rect->top = point->y;
    rect->right = rect->left + width;
    rect->bottom = rect->top + height;
}

void __fastcall move_xy(struct Rectangle *rect, unsigned unused, int x, int y)
{
    int width = rect->right - rect->left;
    int height = rect->bottom - rect->top;
    rect->left = x;
    rect->top = y;
    rect->right = x + width;
    rect->bottom = y + height;
}
