#include "versus_vm_argument.h"

struct Color {
    unsigned char blue, green, red, alpha;
};
struct Canvas {
    unsigned char before_color[0x50];
    struct Color draw_color;
};
void __fastcall versus_add_colors(void *self, unsigned reserved_edx,
                                  struct ArgumentFrame *frame, struct Color *result) {
    struct Color lhs, rhs;
    read_argument(frame, &lhs);
    read_argument(frame, &rhs);
    ++frame->code;
    lhs.red += rhs.red;
    lhs.green += rhs.green;
    lhs.blue += rhs.blue;
    *result = lhs;
}
void __fastcall versus_subtract_colors(void *self, unsigned reserved_edx,
                                       struct ArgumentFrame *frame, struct Color *result) {
    struct Color lhs, rhs;
    read_argument(frame, &lhs);
    read_argument(frame, &rhs);
    ++frame->code;
    lhs.red -= rhs.red;
    lhs.green -= rhs.green;
    lhs.blue -= rhs.blue;
    *result = lhs;
}
void __fastcall versus_set_draw_color(struct Canvas *self, unsigned reserved_edx,
                                      struct ArgumentFrame *frame, void *result) {
    unsigned char r = 0, g, b, a;
    read_argument(frame, &r);
    g = 0;
    read_argument(frame, &g);
    b = 0;
    read_argument(frame, &b);
    a = 0;
    read_argument(frame, &a);
    ++frame->code;
    self->draw_color.red = r;
    self->draw_color.green = g;
    self->draw_color.blue = b;
    self->draw_color.alpha = a ? a : 255;
}
void __fastcall versus_make_color(struct Canvas *self, unsigned reserved_edx,
                                  struct ArgumentFrame *frame, struct Color *result) {
    unsigned char r = 0, g, b, a;
    struct Color color;
    read_argument(frame, &r);
    g = 0;
    read_argument(frame, &g);
    b = 0;
    read_argument(frame, &b);
    a = 0;
    read_argument(frame, &a);
    ++frame->code;
    color.blue = b;
    color.green = g;
    color.red = r;
    color.alpha = a;
    *result = color;
}
