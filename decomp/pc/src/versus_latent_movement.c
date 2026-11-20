#include "versus_script_frame.h"

struct ActorState {
    unsigned char unknown[0x24];
    unsigned latent_action;
};

struct Actor {
    unsigned char unknown[0xc];
    struct ActorState *state;
};

/* Both handlers consume the no-argument bytecode terminator and set the
 * polling action that resumes the script when the movement finishes. */
void __fastcall versus_finish_rotation(struct Actor *actor, unsigned reserved_edx,
                                       struct VersusScriptFrame *frame, void *result) {
    ++frame->code;
    actor->state->latent_action = 509;
}

void __fastcall versus_finish_interpolation(struct Actor *actor, unsigned reserved_edx,
                                            struct VersusScriptFrame *frame, void *result) {
    ++frame->code;
    actor->state->latent_action = 302;
}
