/* Steam Versus stores its skeletal animation channels in a 144-byte array. */
struct animation_channel {
    unsigned char prefix[8];
    int sequence;
    float rate, frame, last_frame, tween_rate;
    unsigned char before_finished[0x10];
    int finished, looping;
    unsigned char before_backward[0x10];
    int backward;
    unsigned char remaining[0x48];
};
struct mesh_instance {
    unsigned char prefix[0x100];
    struct animation_channel *channels;
    int count;
};
#define zero_frame (*(const float *)0x10c109f8u)

static __forceinline int has_channel(const struct mesh_instance *self, int channel)
{
    return self->count > channel && channel >= 0;
}

int __fastcall mesh_channel_looping(const struct mesh_instance *self, void *unused, int index)
{
    return has_channel(self, index) ? self->channels[index].looping : 0;
}

int __fastcall mesh_channel_backward(const struct mesh_instance *self, void *unused, int index)
{
    return has_channel(self, index) ? self->channels[index].backward : 0;
}

float __fastcall mesh_channel_rate(const struct mesh_instance *self, void *unused, int index)
{
    return has_channel(self, index) ? self->channels[index].rate : zero_frame;
}

float __fastcall mesh_channel_frame(const struct mesh_instance *self, void *unused, int index)
{
    return has_channel(self, index) ? self->channels[index].frame : zero_frame;
}

int __fastcall mesh_channel_reached_last_frame(const struct mesh_instance *self, void *unused, int index)
{
    return has_channel(self, index) && self->channels[index].frame >= self->channels[index].last_frame;
}

int __fastcall mesh_channel_stop_looping(struct mesh_instance *self, void *unused, int index)
{
    if (has_channel(self, index)) {
        self->channels[index].looping = 0;
        self->channels[index].finished = 0;
        return 1;
    }
    return 0;
}

int *__fastcall mesh_channel_sequence(const struct mesh_instance *self, void *unused, int *result, int index)
{
    if (has_channel(self, index)) {
        *result = self->channels[index].sequence;
        return result;
    }
    *result = 0;
    return result;
}
