#include "versus_script_frame.h"

#define FLOAT_ZERO (*(const float *)0x10c109f8u)

struct Trigger;
struct TriggerMethods {
    void *before_deactivate[78];
    void (__fastcall *deactivate)(struct Trigger *);
};
struct Trigger {
    const struct TriggerMethods *methods;
    unsigned char before_time[0x3d4];
    float trigger_time;
    int capture_time;
    unsigned char before_flags[4];
    unsigned flags;
};
void __fastcall versus_trigger_time(struct Trigger *self, unsigned reserved_edx,
                                    struct VersusScriptFrame *frame, float *result) {
    ++frame->code;
    *result = (self->flags & 4) ? self->trigger_time + self->capture_time : FLOAT_ZERO;
}
void __fastcall versus_deactivate_trigger(struct Trigger *self, unsigned reserved_edx,
                                          struct VersusScriptFrame *frame, void *result) {
    ++frame->code;
    self->methods->deactivate(self);
}
