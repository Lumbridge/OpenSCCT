#include "versus_vm_argument.h"

struct StackInt { int value; };
struct StackByte { unsigned char value; };
struct Sound;
struct StackSound { struct Sound *value; };
struct AudioDevice;
struct AudioMethods {
    void *before_stop_music[32];
    void (__fastcall *stop_music)(struct AudioDevice *, struct StackSound song,
                                  struct StackInt song_handle, float fade_time);
    void *before_change[3];
    void (__fastcall *change_chatter)(struct AudioDevice *, struct StackByte id, struct StackInt add);
    void *before_mute[2];
    void (__fastcall *mute_chatter)(struct AudioDevice *, struct StackByte id, struct StackInt mute);
};
struct AudioDevice {
    const struct AudioMethods *methods;
};
struct Engine {
    unsigned char before_audio[0x34];
    struct AudioDevice *audio;
};
struct Level {
    unsigned char before_engine[0xf0];
    struct Engine *engine;
};
struct AudioActor {
    unsigned char before_level[0x1a4];
    struct Level *level;
};

void __fastcall versus_stop_music(struct AudioActor *self, unsigned reserved_edx,
                                  struct ArgumentFrame *frame, void *result) {
    struct StackSound song = {0};
    struct StackInt handle;
    float fade_time;
    struct AudioDevice *device;
    read_argument(frame, &song);
    handle.value = 0;
    read_argument(frame, &handle);
    fade_time = 0;
    read_argument(frame, &fade_time);
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        device->methods->stop_music(device, song, handle, fade_time);
    }
}

void __fastcall versus_change_voice_chatter(struct AudioActor *self, unsigned reserved_edx,
                                            struct ArgumentFrame *frame, void *result) {
    struct StackByte id = {0};
    struct StackInt add;
    struct AudioDevice *device;
    read_argument(frame, &id);
    add.value = 0;
    read_argument(frame, &add);
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        device->methods->change_chatter(device, id, add);
    }
}

void __fastcall versus_mute_voice_chatter(struct AudioActor *self, unsigned reserved_edx,
                                          struct ArgumentFrame *frame, void *result) {
    struct StackByte id = {0};
    struct StackInt mute;
    struct AudioDevice *device;
    read_argument(frame, &id);
    mute.value = 0;
    read_argument(frame, &mute);
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        device->methods->mute_chatter(device, id, mute);
    }
}
