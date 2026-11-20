#include "versus_script_frame.h"

struct Objective {
    unsigned char unknown[0x3e4];
    unsigned flags;
};

void __fastcall versus_objective_is_active(struct Objective *self, unsigned reserved_edx,
                                           struct VersusScriptFrame *stack, unsigned *result) {
    ++stack->code;
    *result = (~(self->flags >> 1)) & 1u;
}
