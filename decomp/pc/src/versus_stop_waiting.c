#include "versus_script_frame.h"

struct StateFrame {
    unsigned char unknown[0x24];
    int latent_action;
};
struct Controller {
    unsigned char unknown[12];
    struct StateFrame *state;
    unsigned char gap[0x1e8];
    float move_timer;
};

void __fastcall versus_stop_waiting(struct Controller *self, unsigned reserved_edx,
                                    struct VersusScriptFrame *frame, void *result) {
    ++frame->code;
    if (self->state->latent_action == 384) {
        self->move_timer = -1.0f;
    }
}
