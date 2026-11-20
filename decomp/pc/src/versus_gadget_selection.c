#include "versus_script_frame.h"

struct MineSelector {
    unsigned char unknown[0x350];
    unsigned char available;
    unsigned char gap[0xb7];
    int selected;
};

struct CamOwner {
    unsigned char unknown[0x604];
    void *active;
};

struct ComponentState {
    unsigned char unknown[0x104];
    struct CamOwner *owner;
};

void __fastcall versus_next_mine_type(struct MineSelector *self, unsigned reserved_edx,
                                      struct VersusScriptFrame *stack, void *result) {
    ++stack->code;
    if (++self->selected >= self->available) {
        self->selected = 0;
    }
}

void __fastcall versus_previous_mine_type(struct MineSelector *self, unsigned reserved_edx,
                                          struct VersusScriptFrame *stack, void *result) {
    ++stack->code;
    if (--self->selected < 0) {
        self->selected = self->available - 1;
    }
}

void __fastcall versus_sticky_cam_can_use(struct ComponentState *self, unsigned reserved_edx,
                                          struct VersusScriptFrame *stack, int *result) {
    ++stack->code;
    *result = self->owner->active == 0;
}
