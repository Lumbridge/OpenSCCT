#include "versus_script_frame.h"

struct UnrealClass {
    unsigned char unknown[0x28];
    struct UnrealClass *super;
};
struct PlayerDevice;
struct InputMethods {
    void *before_screenshot[23];
    void (__fastcall *screenshot)(struct PlayerDevice *);
    void *before_rumble;
    void (__fastcall *rumble_stop)(struct PlayerDevice *, unsigned reserved_edx, int pad);
    void *before_locked_pad[6];
    int (__fastcall *locked_pad)(struct PlayerDevice *);
};
struct PlayerDevice {
    const struct InputMethods *methods;
    unsigned char before_class[0x20];
    struct UnrealClass *class_type;
};
struct PlayerController {
    unsigned char before_device[0x4a4];
    struct PlayerDevice *device;
};
#define VIEWPORT_CLASS ((struct UnrealClass *)0x10cbc930u)
static __inline int is_viewport(const struct PlayerDevice *device) {
    struct UnrealClass *type;
    if (device) {
        for (type = device->class_type; type; type = type->super) {
            if (type == VIEWPORT_CLASS) {
                return 1;
            }
        }
    }
    return 0;
}

void __fastcall versus_player_screenshot(struct PlayerController *self, unsigned reserved_edx,
                                         struct VersusScriptFrame *frame, void *result) {
    struct PlayerDevice *device;
    ++frame->code;
    device = self->device;
    if (device) {
        device->methods->screenshot(device);
    }
}
void __fastcall versus_rumble_pad_stop(struct PlayerController *self, unsigned reserved_edx,
                                       struct VersusScriptFrame *frame, void *result) {
    struct PlayerDevice *device;
    ++frame->code;
    device = self->device;
    if (is_viewport(device)) {
        device->methods->rumble_stop(device, reserved_edx, -1);
    }
}
void __fastcall versus_get_locked_pad(struct PlayerController *self, unsigned reserved_edx,
                                      struct VersusScriptFrame *frame, int *result) {
    struct PlayerDevice *device;
    ++frame->code;
    device = self->device;
    if (is_viewport(device)) {
        *result = device->methods->locked_pad(device);
    } else {
        *result = -1;
    }
}
