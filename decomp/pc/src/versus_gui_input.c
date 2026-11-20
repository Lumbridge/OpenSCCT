#include "versus_vm_argument.h"

struct Viewport;
struct InputMethods {
    void *before_input[31];
    void (__fastcall *input)(struct Viewport *, const struct InputMethods *dispatch,
                             void *device, int key, int state, float delta);
};
struct Viewport { const struct InputMethods *methods; };
struct InputContext {
    unsigned char before_viewport[0x28];
    struct Viewport *viewport;
    void **device;
};
struct GUIViewport {
    unsigned char before_context[0x28];
    struct InputContext *context;
};
struct GUIController {
    unsigned char before_viewport[0x30];
    struct GUIViewport *viewport;
};
void __fastcall versus_cause_input_event(struct GUIController *self, unsigned reserved_edx,
                                         struct ArgumentFrame *frame, void *result) {
    unsigned char key = 0, state;
    float delta;
    struct InputContext *context;
    struct Viewport *view;
    read_argument(frame, &key);
    state = 0;
    read_argument(frame, &state);
    delta = 0;
    read_argument(frame, &delta);
    ++frame->code;
    context = self->viewport->context;
    view = context->viewport;
    view->methods->input(view, view->methods, *context->device, key, state, delta);
}
