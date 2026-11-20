#include "versus_vm_argument.h"

/* Retail places the dispatch table in EDX before these x86 member calls. */
struct Actor;
struct ActorMethods {
    void *before_play[73];
    void (__fastcall *play)(struct Actor *, const struct ActorMethods *dispatch, int channel, int sequence,
                            float rate, float tween_time, int loop, int backwards,
                            int continue_at_frame, int extra);
    void (__fastcall *play_after)(struct Actor *, const struct ActorMethods *dispatch, int channel, int sequence,
                                  float rate, float time, int loop, int backwards);
};
struct Actor {
    const struct ActorMethods *methods;
};

void __fastcall versus_play_animation(struct Actor *self, unsigned reserved_edx,
                                      struct ArgumentFrame *frame, void *result) {
    int sequence = 0;
    float rate;
    float tween;
    int channel;
    int backwards;
    int continue_playing;
    read_argument(frame, &sequence);
    rate = 1.0f;
    read_argument(frame, &rate);
    tween = 0;
    read_argument(frame, &tween);
    channel = 0;
    read_argument(frame, &channel);
    backwards = 0;
    read_argument(frame, &backwards);
    continue_playing = 0;
    read_argument(frame, &continue_playing);
    ++frame->code;
    self->methods->play(self, self->methods, channel, sequence, rate, tween,
                              0, backwards, continue_playing, 0);
}

void __fastcall versus_loop_animation(struct Actor *self, unsigned reserved_edx,
                                      struct ArgumentFrame *frame, void *result) {
    int sequence = 0;
    float rate;
    float tween;
    int channel;
    int backwards;
    int continue_playing;
    read_argument(frame, &sequence);
    rate = 1.0f;
    read_argument(frame, &rate);
    tween = 0;
    read_argument(frame, &tween);
    channel = 0;
    read_argument(frame, &channel);
    backwards = 0;
    read_argument(frame, &backwards);
    continue_playing = 0;
    read_argument(frame, &continue_playing);
    ++frame->code;
    self->methods->play(self, self->methods, channel, sequence, rate, tween,
                              1, backwards, continue_playing, 0);
}
