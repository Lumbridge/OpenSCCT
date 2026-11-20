#include "versus_vm_argument.h"

struct Color {
    unsigned char blue, green, red, alpha;
};

void __fastcall versus_multiply_color_float(void *self, unsigned reserved_edx,
                                            struct ArgumentFrame *frame, struct Color *result) {
    struct Color color;
    float scale;
    read_argument(frame, &color);
    scale = 0;
    read_argument(frame, &scale);
    ++frame->code;
    color.red = (unsigned char)(int)(color.red * scale);
    color.green = (unsigned char)(int)(color.green * scale);
    color.blue = (unsigned char)(int)(color.blue * scale);
    *result = color;
}

void __fastcall versus_multiply_float_color(void *self, unsigned reserved_edx,
                                            struct ArgumentFrame *frame, struct Color *result) {
    float scale = 0;
    struct Color color;
    read_argument(frame, &scale);
    read_argument(frame, &color);
    ++frame->code;
    color.red = (unsigned char)(int)(color.red * scale);
    color.green = (unsigned char)(int)(color.green * scale);
    color.blue = (unsigned char)(int)(color.blue * scale);
    *result = color;
}
