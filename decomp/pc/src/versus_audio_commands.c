#include "versus_vm_argument.h"

struct Sound;
struct SoundArg { struct Sound *sound; };
struct Actor;
struct ActorArg { struct Actor *actor; };
struct AudioDevice;
struct AudioMethods {
    void *before_stop[0x74 / 4];
    void (__fastcall *stop_sound)(struct AudioDevice *, struct ActorArg, struct SoundArg);
};
struct AudioDevice { const struct AudioMethods *methods; };
struct Engine { unsigned char before_audio[0x34]; struct AudioDevice *audio; };
struct Level { unsigned char before_engine[0xf0]; struct Engine *engine; };
struct Actor { unsigned char before_level[0x1a4]; struct Level *level; };

void __fastcall execStopSound(struct Actor *self, unsigned int unused_edx,
                               struct ArgumentFrame *frame, void *result)
{
    struct SoundArg sound = {0};
    struct ActorArg owner;
    struct AudioDevice *device;
    read_argument(frame, &sound);
    ++frame->code;
    device = self->level->engine->audio;
    if (device) {
        owner.actor = self;
        device->methods->stop_sound(device, owner, sound);
    }
}
