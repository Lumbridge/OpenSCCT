#include "versus_vm_argument.h"

struct CameraNetwork {
    unsigned char before_energy[0x40c];
    float energy;
};
void __fastcall versus_authorize_energy(struct CameraNetwork *self, unsigned reserved_edx,
                                        struct ArgumentFrame *frame, int *result) {
    int required = 0;
    read_argument(frame, &required);
    ++frame->code;
    if (required > self->energy) {
        *result = 0;
    } else {
        self->energy -= required;
        *result = 1;
    }
}
