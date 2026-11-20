/* The component caches the canvas pen state while a widget draws itself. */
struct font;
struct color { unsigned char blue, green, red, alpha; };

struct canvas_pen {
    unsigned char object_fields[0x28];
    struct font *font;
    unsigned char canvas_fields[0x10];
    float x, y, z;
    unsigned char style;
    unsigned char before_color[7];
    struct color color;
};

struct gui_component {
    unsigned char object_fields[0x70];
    float top, left, width, height;
    unsigned char component_fields[0x54];
    float saved_x, saved_y;
    struct color saved_color;
    struct font *saved_font;
    unsigned char saved_style;
};

void __fastcall gui_save_canvas_state(struct gui_component *self, void *unused, const struct canvas_pen *canvas)
{
    self->saved_x = canvas->x;
    self->saved_y = canvas->y;
    self->saved_color = canvas->color;
    self->saved_style = canvas->style;
    self->saved_font = canvas->font;
}

void __fastcall gui_restore_canvas_state(const struct gui_component *self, void *unused, struct canvas_pen *canvas)
{
    canvas->x = self->saved_x;
    canvas->y = self->saved_y;
    canvas->color = self->saved_color;
    canvas->style = self->saved_style;
    canvas->font = self->saved_font;
}

void __fastcall gui_set_dimensions(struct gui_component *self, void *unused, float width, float height, float left, float top)
{
    self->width = width;
    self->height = height;
    self->left = left;
    self->top = top;
}
