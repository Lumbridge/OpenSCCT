#include "versus_script_frame.h"

struct Component;
typedef float (__fastcall *Measure)(struct Component *);
typedef void (__fastcall *BeginPage)(struct Component *);
struct GuiMethods {
    void *before_init[14];
    BeginPage init;
    void *before_width[11];
    Measure width;
    Measure height;
    Measure left;
    Measure top;
};
struct Component {
    const struct GuiMethods *methods;
};

void __fastcall versus_gui_actual_width(struct Component *self, unsigned reserved_edx,
                                        struct VersusScriptFrame *frame, float *result) {
    ++frame->code;
    *result = self->methods->width(self);
}

void __fastcall versus_gui_actual_height(struct Component *self, unsigned reserved_edx,
                                        struct VersusScriptFrame *frame, float *result) {
    ++frame->code;
    *result = self->methods->height(self);
}

void __fastcall versus_gui_actual_left(struct Component *self, unsigned reserved_edx,
                                        struct VersusScriptFrame *frame, float *result) {
    ++frame->code;
    *result = self->methods->left(self);
}

void __fastcall versus_gui_actual_top(struct Component *self, unsigned reserved_edx,
                                        struct VersusScriptFrame *frame, float *result) {
    ++frame->code;
    *result = self->methods->top(self);
}

void __fastcall versus_gui_init_page(struct Component *self, unsigned reserved_edx,
                                     struct VersusScriptFrame *frame, void *result) {
    ++frame->code;
    self->methods->init(self);
}
