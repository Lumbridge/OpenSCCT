#include "versus_vm_argument.h"

struct Target {
    unsigned char unknown[0x1bc];
    void *mesh;
    unsigned char gap[0x14];
    void *mesh_instance;
};
struct CameraController {
    unsigned char unknown[0x774];
    struct Target *pawn;
    unsigned char gap[4];
    struct Target *spectator_target;
};
struct CameraLevel {
    unsigned char unknown[0x410];
    void *pauser;
};
struct Camera {
    unsigned char before_level[0x1a0];
    struct CameraLevel *level;
    unsigned char before_controller[0x1620];
    struct CameraController *controller;
    struct Target *followed_pawn;
    unsigned char before_flags[0xc];
    unsigned flags;
};
void __fastcall versus_following_rotation(struct Camera *self, unsigned reserved_edx,
                                          struct ArgumentFrame *frame, void *result) {
    struct Target *view_actor = 0;
    struct Target *target;
    read_argument(frame, &view_actor);
    ++frame->code;
    if (self->level->pauser) {
        return;
    }
    target = self->controller->spectator_target;
    if (self->controller->pawn) {
        target = self->controller->pawn;
    }
    if (target == view_actor && target->mesh && target->mesh_instance) {
        self->followed_pawn = target;
        self->flags |= 1;
    } else {
        self->flags &= ~1u;
    }
}
