#include "versus_vm_argument.h"
struct Emitter {
    unsigned char before_count[0x3e0];
    int pending;
};
struct Gadget {
    unsigned char before_charge[0x330];
    int charge;
};
struct HUD {
    unsigned char before_slot[0x7f8];
    int forced_slot;
};
void __fastcall versus_spawn_particle(struct Emitter *self, unsigned reserved_edx,
                                      struct ArgumentFrame *frame, void *result) {
    int count = 0;
    read_argument(frame, &count);
    ++frame->code;
    self->pending += count;
}
void __fastcall versus_sfx_force_slot(struct HUD *self, unsigned reserved_edx,
                                      struct ArgumentFrame *frame, void *result) {
    int slot = 0;
    read_argument(frame, &slot);
    ++frame->code;
    self->forced_slot = slot;
}
void __fastcall versus_gadget_add_charge(struct Gadget *self, unsigned reserved_edx,
                                         struct ArgumentFrame *frame, void *result) {
    unsigned char amount = 1;
    read_argument(frame, &amount);
    ++frame->code;
    self->charge += amount;
}
void __fastcall versus_gadget_decrement_charge(struct Gadget *self, unsigned reserved_edx,
                                               struct ArgumentFrame *frame, void *result) {
    unsigned char amount = 1;
    read_argument(frame, &amount);
    ++frame->code;
    self->charge -= amount;
}
