#include "versus_vm_argument.h"

struct Viewport {
    unsigned char before_first_person[0x18c];
    void *first_person;
};
struct Canvas {
    unsigned char before_viewport[0x78];
    struct Viewport *viewport;
};
void __fastcall versus_draw_first_person(struct Canvas *self, unsigned reserved_edx,
                                         struct ArgumentFrame *frame, void *result) {
    void *actor = 0;
    read_argument(frame, &actor);
    ++frame->code;
    self->viewport->first_person = actor;
}
