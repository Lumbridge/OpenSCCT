#include "versus_vm_argument.h"

struct StateFrame {
    unsigned char unknown[0x24];
    int latent_action;
};
struct SleepActor {
    unsigned char unknown[12];
    struct StateFrame *state;
    unsigned char before_timer[0x1e8];
    float sleep_time;
};
struct SleepPage {
    unsigned char unknown[12];
    struct StateFrame *state;
    unsigned char before_timer[0x1e0];
    float sleep_time;
};
struct ActorTimer {
    unsigned char before_rate[0x1b8];
    float rate;
    unsigned char before_counter[0x14];
    float counter;
    unsigned char before_flags[0x114];
    unsigned reserved_flags : 18;
    unsigned looping : 1;
};
void __fastcall versus_actor_sleep(struct SleepActor *self, unsigned reserved_edx,
                                   struct ArgumentFrame *frame, void *result) {
    float duration = 0;
    read_argument(frame, &duration);
    ++frame->code;
    self->state->latent_action = 384;
    self->sleep_time = duration;
}
void __fastcall versus_gui_page_sleep(struct SleepPage *self, unsigned reserved_edx,
                                      struct ArgumentFrame *frame, void *result) {
    float duration = 0;
    read_argument(frame, &duration);
    ++frame->code;
    self->state->latent_action = 333;
    self->sleep_time = duration;
}
void __fastcall versus_in_latent_execution(struct SleepActor *self, unsigned reserved_edx,
                                           struct ArgumentFrame *frame, int *result) {
    int action = 0;
    read_argument(frame, &action);
    ++frame->code;
    *result = self->state->latent_action == action;
}
void __fastcall versus_schedule_objective_reset(struct ActorTimer *self, unsigned reserved_edx,
                                                struct ArgumentFrame *frame, void *result) {
    int duration = 0;
    read_argument(frame, &duration);
    ++frame->code;
    self->rate = (float)duration;
    self->counter = 0;
    self->looping = 0;
}
void __fastcall versus_actor_set_timer(struct ActorTimer *self, unsigned reserved_edx,
                                       struct ArgumentFrame *frame, void *result) {
    float duration = 0;
    int looping;
    read_argument(frame, &duration);
    looping = 0;
    read_argument(frame, &looping);
    ++frame->code;
    self->counter = 0;
    self->rate = duration;
    self->looping = looping;
}
