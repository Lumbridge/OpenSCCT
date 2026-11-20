#include "versus_vm_argument.h"

struct CameraMode {
    float offsets[4][3];
    float distance[4];
    unsigned char before_collision[0x50];
    int use_collision;
    unsigned char remaining[4];
};
struct ModeCamera {
    unsigned char before_modes[0x2f8];
    struct CameraMode settings[35];
    unsigned char current;
    unsigned char volume_size;
    unsigned char before_distance[0xe];
    float previous_distance;
    float previous_ideal_distance;
    unsigned char before_smoothing[0x60];
    unsigned smoothing_flags;
};
void __fastcall versus_camera_set_mode(struct ModeCamera *self, unsigned reserved_edx,
                                       struct ArgumentFrame *frame, void *result) {
    unsigned char requested = 0;
    read_argument(frame, &requested);
    ++frame->code;
    if (self->current == requested) {
        return;
    }
    if (self->current == 34) {
        self->smoothing_flags |= 1;
    }
    if (!((self->current == 0 && requested == 2) || (self->current == 1 && requested == 3))) {
        if (self->settings[self->current].use_collision && !self->settings[requested].use_collision) {
            self->previous_ideal_distance = self->previous_distance;
        } else {
            self->previous_ideal_distance = self->settings[requested].distance[self->volume_size];
        }
    }
    self->current = requested;
}
