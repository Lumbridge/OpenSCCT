#include "versus_vm_argument.h"

struct UnrealClass {
    unsigned char before_base[0x28];
    struct UnrealClass *base;
};

struct Mesh {
    unsigned char before_class[0x24];
    struct UnrealClass *type;
};

struct MeshInstance;
struct Actor;
struct ActorArg { struct Actor *actor; };

struct MeshArg { struct Mesh *mesh; };

struct IntArg { int value; };

struct FloatArg { float value; };
typedef int (__fastcall *ChannelQuery)(struct MeshInstance *, struct IntArg);
struct MeshOps {
    void *before_rebind[33];
    void (__fastcall *rebind)(struct MeshInstance *, struct MeshArg);
    void *before_animating[17];
    ChannelQuery is_animating;
    void *next;
    void (__fastcall *freeze)(struct MeshInstance *, struct FloatArg, struct IntArg);
    ChannelQuery is_tweening;
    void *after_tweening;
    ChannelQuery is_backward;
    void *before_set_frame[5];
    void (__fastcall *set_frame)(struct MeshInstance *, struct IntArg, struct FloatArg, struct IntArg);
    void *before_refresh[11];
    void (__fastcall *refresh)(struct MeshInstance *);
};

struct MeshInstance {
    const struct MeshOps *ops;
    unsigned char before_notify[0x12c];
    int notify_channel;
};

struct Actor {
    unsigned char before_mesh[0x1bc];
    struct Mesh *mesh;
    unsigned char before_instance[0x14];
    struct MeshInstance *instance;
    unsigned char before_draw_type[0x101];
    unsigned char draw_type;
    unsigned char before_flags[0x12];
    unsigned flags;
};
/* A one-field aggregate is stack-passed in Microsoft's x86 fastcall ABI;
 * this models a member function's explicit argument after the ECX object. */
extern struct MeshInstance *__fastcall versus_mesh_instance(struct Mesh *, struct ActorArg);
static __inline struct MeshInstance *get_instance(struct Actor *self) {
    struct ActorArg arg;
    arg.actor = self;
    return versus_mesh_instance(self->mesh,arg);
}

void __fastcall versus_anim_tweening(struct Actor *self, unsigned reserved_edx,
                                     struct ArgumentFrame *frame, int *result) {
    struct IntArg channel;
    channel.value = 0;
    read_argument(frame, &channel.value);
    ++frame->code;
    *result = 0;
    if (self->mesh) {
        get_instance(self);
        *result = self->instance->ops->is_tweening(self->instance, channel) != 0;
    }
}

void __fastcall versus_anim_backward(struct Actor *self, unsigned reserved_edx,
                                     struct ArgumentFrame *frame, int *result) {
    struct IntArg channel;
    channel.value = 0;
    read_argument(frame, &channel.value);
    ++frame->code;
    *result = 0;
    if (self->mesh) {
        get_instance(self);
        *result = self->instance->ops->is_backward(self->instance, channel) != 0;
    }
}

static __inline int animating(struct Actor *actor, struct IntArg channel) {
    if (actor->draw_type == 2 && actor->mesh) {
        get_instance(actor);
        return actor->instance->ops->is_animating(actor->instance, channel);
    }
    return 0;
}

void __fastcall versus_is_animating(struct Actor *self, unsigned reserved_edx,
                                    struct ArgumentFrame *frame, int *result) {
    int channel = 0;
    struct IntArg arg;
    read_argument(frame, &channel);
    ++frame->code;
    arg.value = channel;
    *result = animating(self, arg);
}

void __fastcall versus_freeze_anim_at(struct Actor *self, unsigned reserved_edx,
                                      struct ArgumentFrame *frame, void *result) {
    struct FloatArg time;
    struct IntArg channel;
    time.value = 0;
    read_argument(frame, &time.value);
    channel.value = 0;
    read_argument(frame, &channel.value);
    ++frame->code;
    if (self->mesh) {
        get_instance(self);
        self->instance->ops->freeze(self->instance, time, channel);
    }
}

void __fastcall versus_set_anim_frame(struct Actor *self, unsigned reserved_edx,
                                      struct ArgumentFrame *frame, void *result) {
    struct FloatArg time;
    struct IntArg channel, unit_flag;
    time.value = 0;
    read_argument(frame, &time.value);
    channel.value = 0;
    read_argument(frame, &channel.value);
    unit_flag.value = 0;
    read_argument(frame, &unit_flag.value);
    ++frame->code;
    if (self->mesh) {
        get_instance(self);
        self->instance->ops->set_frame(self->instance, channel, time, unit_flag);
    }
}

static __inline int is_skeletal(struct Mesh *mesh) {
    struct UnrealClass *type;
    if (mesh) {
        for (type = mesh->type; type; type = type->base) {
            if (type == (struct UnrealClass *)0x10cc0a78u) {
                return 1;
            }
        }
    }
    return 0;
}

void __fastcall versus_refresh_bones(struct Actor *self, unsigned reserved_edx,
                                     struct ArgumentFrame *frame, void *result) {
    struct MeshInstance *inst;
    ++frame->code;
    if (is_skeletal(self->mesh)) {
        inst = get_instance(self);
        inst->ops->refresh(inst);
    }
}

void __fastcall versus_notify_channel(struct Actor *self, unsigned reserved_edx,
                                      struct ArgumentFrame *frame, int *result) {
    ++frame->code;
    if (is_skeletal(self->mesh)) {
        *result = get_instance(self)->notify_channel;
    } else {
        *result = 0;
    }
}
extern void __stdcall versus_lock_root(struct MeshInstance *, int locked);
void __fastcall versus_root_motion_lock(struct Actor *self, unsigned reserved_edx,
                                        struct ArgumentFrame *frame, void *result) {
    int locked = 1;
    read_argument(frame, &locked);
    ++frame->code;
    if (is_skeletal(self->mesh)) {
        get_instance(self);
        versus_lock_root(self->instance, locked);
    }
}
extern void __fastcall versus_blend_channel(int, struct MeshInstance *, float, float);
void __fastcall versus_blend_to_alpha(struct Actor *self, unsigned reserved_edx,
                                      struct ArgumentFrame *frame, void *result) {
    int stage = 0;
    float alpha, time;
    read_argument(frame, &stage);
    alpha = 0;
    read_argument(frame, &alpha);
    time = 0;
    read_argument(frame, &time);
    ++frame->code;
    if (is_skeletal(self->mesh)) {
        get_instance(self);
        versus_blend_channel(stage, self->instance, alpha, time);
    }
}

void __fastcall versus_link_mesh(struct Actor *self, unsigned reserved_edx,
                                 struct ArgumentFrame *frame, void *result) {
    struct MeshArg replacement;
    int keep_animation;
    replacement.mesh = 0;
    read_argument(frame, &replacement.mesh);
    keep_animation = 0;
    read_argument(frame, &keep_animation);
    ++frame->code;
    if (self->mesh != replacement.mesh) {
        if (is_skeletal(replacement.mesh) && keep_animation) {
            struct MeshInstance *instance = get_instance(self);
            instance->ops->rebind(instance, replacement);
        }
        self->mesh = replacement.mesh;
        self->flags |= 4;
    }
}
