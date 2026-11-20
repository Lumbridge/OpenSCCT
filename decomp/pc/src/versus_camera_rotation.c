#include "versus_script_frame.h"

struct LevelRotation {
    unsigned char unknown[0x410];
    void *pauser;
};

struct ViewRotation {
    unsigned char unknown[0xe0];
    int pitch;
    int yaw;
};

struct Camera {
    unsigned char unknown[0x1a0];
    struct LevelRotation *level;
    unsigned char before_controller[0x1620];
    struct ViewRotation *controller;
    unsigned char before_angles[0xc4];
    int pitch;
    int yaw;
};

static __inline int magnitude(int v) {
    return v < 0 ? -v : v;
}

void __fastcall versus_resetting_rotation(struct Camera *self, unsigned reserved_edx,
                                          struct VersusScriptFrame *stack, int *result) {
    ++stack->code;
    if (self->level->pauser) {
        *result = 0;
        return;
    }
    *result = magnitude(self->controller->yaw - self->yaw) < 5 &&
              magnitude(self->controller->pitch - self->pitch) < 5;
}
