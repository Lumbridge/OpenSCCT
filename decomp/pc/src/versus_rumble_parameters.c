#include "versus_vm_argument.h"

struct UnrealClass {
    unsigned char unknown[0x28];
    struct UnrealClass *super;
};
struct PlayerDevice;
/* The unused EDX position carries the dispatch table, leaving explicit arguments on the x86 member stack. */
struct InputMethods {
    void *before_start[24];
    void (__fastcall *start)(struct PlayerDevice *, const struct InputMethods *dispatch, int pad, int low,
                             int high, float duration);
    void *stop;
    void (__fastcall *update)(struct PlayerDevice *, const struct InputMethods *dispatch, int pad, int low,
                              int high);
    void (__fastcall *set_time)(struct PlayerDevice *, const struct InputMethods *dispatch, int pad, float duration);
    void (__fastcall *enable)(struct PlayerDevice *, const struct InputMethods *dispatch, int enabled);
    void (__fastcall *test)(struct PlayerDevice *, const struct InputMethods *dispatch, int pad, int low,
                            int high, float duration);
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

void __fastcall versus_rumble_test(struct PlayerController *self, unsigned reserved_edx,
                                   struct ArgumentFrame *frame, void *result) {
    int low = 0;
    int high;
    float duration;
    struct PlayerDevice *device;
    read_argument(frame, &low);
    high = 0;
    read_argument(frame, &high);
    duration = 0.0f;
    read_argument(frame, &duration);
    ++frame->code;
    device = self->device;
    if (is_viewport(device)) {
        device->methods->test(device, device->methods, -1, low, high, duration);
    }
}

void __fastcall versus_rumble_update(struct PlayerController *self, unsigned reserved_edx,
                                     struct ArgumentFrame *frame, void *result) {
    int low = 0;
    int high;
    struct PlayerDevice *device;
    read_argument(frame, &low);
    high = 0;
    read_argument(frame, &high);
    ++frame->code;
    device = self->device;
    if (is_viewport(device)) {
        device->methods->update(device, device->methods, -1, low, high);
    }
}
