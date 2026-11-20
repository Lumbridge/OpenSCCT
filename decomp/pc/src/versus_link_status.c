#include "versus_script_frame.h"

struct InternetLink {
    unsigned char unknown[0x30c];
    int pending;
};

void __fastcall versus_is_data_pending(struct InternetLink *self, unsigned reserved_edx,
                                       struct VersusScriptFrame *stack, int *result) {
    ++stack->code;
    *result = self->pending != 0;
}
