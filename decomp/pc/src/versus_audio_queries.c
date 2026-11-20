#include "versus_script_frame.h"

struct AudioDevice;
typedef void (__fastcall *AudioAction)(struct AudioDevice *);
typedef float (__fastcall *ReadVolume)(struct AudioDevice *);
typedef int (__fastcall *ReadAudioInt)(struct AudioDevice *);
typedef unsigned char (__fastcall *ReadAudioByte)(struct AudioDevice *);

struct AudioMethods {
    void *before_current_music[34];
    ReadAudioInt current_music;
    void *before_enter_chat[2];
    AudioAction enter_chat;
    AudioAction leave_chat;
    void *before_voice_mask[4];
    ReadAudioByte voice_mask;
    void *before_voice_to_speakers;
    ReadAudioInt voice_to_speakers;
    void *before_sound[6];
    ReadVolume sound_volume;
    void *before_music;
    ReadVolume music_volume;
    void *before_ambient;
    ReadVolume ambient_volume;
    void *before_voice;
    ReadVolume voice_volume;
    void *before_eax;
    ReadAudioInt eax_enabled;
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

void __fastcall versus_sound_volume(struct AudioActor *self, unsigned reserved_edx,
                                    struct VersusScriptFrame *frame, float *result) {
    struct AudioDevice *device;
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        *result = device->methods->sound_volume(device);
    } else {
        *result = 0;
    }
}

void __fastcall versus_music_volume(struct AudioActor *self, unsigned reserved_edx,
                                    struct VersusScriptFrame *frame, float *result) {
    struct AudioDevice *device;
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        *result = device->methods->music_volume(device);
    } else {
        *result = 0;
    }
}

void __fastcall versus_ambient_volume(struct AudioActor *self, unsigned reserved_edx,
                                      struct VersusScriptFrame *frame, float *result) {
    struct AudioDevice *device;
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        *result = device->methods->ambient_volume(device);
    } else {
        *result = 0;
    }
}

void __fastcall versus_voice_volume(struct AudioActor *self, unsigned reserved_edx,
                                    struct VersusScriptFrame *frame, float *result) {
    struct AudioDevice *device;
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        *result = device->methods->voice_volume(device);
    } else {
        *result = 0;
    }
}

void __fastcall versus_current_music(struct AudioActor *self, unsigned reserved_edx,
                                     struct VersusScriptFrame *frame, int *result) {
    struct AudioDevice *device;
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        *result = device->methods->current_music(device);
    }
}

void __fastcall versus_enter_chat(struct AudioActor *self, unsigned reserved_edx,
                                  struct VersusScriptFrame *frame, void *result) {
    struct AudioDevice *device;
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        device->methods->enter_chat(device);
    }
}

void __fastcall versus_leave_chat(struct AudioActor *self, unsigned reserved_edx,
                                  struct VersusScriptFrame *frame, void *result) {
    struct AudioDevice *device;
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        device->methods->leave_chat(device);
    }
}

void __fastcall versus_voice_mask(struct AudioActor *self, unsigned reserved_edx,
                                  struct VersusScriptFrame *frame, unsigned char *result) {
    struct AudioDevice *device;
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        *result = device->methods->voice_mask(device);
    } else {
        *result = 0;
    }
}

void __fastcall versus_voice_to_speakers(struct AudioActor *self, unsigned reserved_edx,
                                         struct VersusScriptFrame *frame, int *result) {
    struct AudioDevice *device;
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        *result = device->methods->voice_to_speakers(device);
    } else {
        *result = 0;
    }
}

void __fastcall versus_eax_enabled(struct AudioActor *self, unsigned reserved_edx,
                                   struct VersusScriptFrame *frame, volatile int *result) {
    struct AudioDevice *device;
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        *result = device->methods->eax_enabled(device);
    }
    *result = 0;
}
