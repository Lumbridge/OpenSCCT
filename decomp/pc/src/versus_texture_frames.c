union UpdateTime { double seconds; unsigned words[2]; };
struct Texture;
struct TextureMethods {
    void *before_tick[40];
    void (__fastcall *tick)(struct Texture *, float delta);
    void (__fastcall *advance)(struct Texture *);
    void *before_update[2];
    void (__fastcall *update)(struct Texture *, union UpdateTime now);
    void (__fastcall *prime)(struct Texture *);
};
struct Mip {
    unsigned char before_data[0x1c];
    void *pixels;
    unsigned length, capacity;
};
#pragma pack(push, 4)
struct Texture {
    const struct TextureMethods *methods;
    unsigned char before_time[0x60];
    union UpdateTime last_update;
    void *render_interface;
    struct Mip *mips;
    int mip_count, mip_capacity;
    struct Texture *anim_next, *anim_current;
    float min_frame_rate, max_frame_rate, accumulator;
    unsigned char before_flags[0xb4-0x90];
    unsigned flags;
    unsigned char lod_set, prime_count, prime_current;
};
#pragma pack(pop)
void __fastcall versus_texture_update(struct Texture *self, union UpdateTime now) {
    /* Texture exposes this timestamp as two native words. */
    if (now.seconds > self->last_update.seconds) {
        if (self->flags & 8) {
            self->flags |= 32;
        }
        self->methods->tick(self, (float)(now.seconds - self->last_update.seconds));
        self->last_update = now;
    } else {
        self->last_update.seconds = now.seconds;
    }
}
void __fastcall versus_texture_prime(struct Texture *self) {
    while (self->prime_current < self->prime_count) {
        ++self->prime_current;
        self->methods->advance(self);
    }
}
void __fastcall versus_texture_advance(struct Texture *self) {
    if (!self->anim_current) {
        self->anim_current = self;
    }
    self->anim_current = self->anim_current->anim_next;
    if (!self->anim_current) {
        self->anim_current = self;
    }
}
struct ViewportArg { void *viewport; };
struct Texture *__fastcall versus_texture_frame(struct Texture *self, union UpdateTime now, struct ViewportArg viewport) {
    self->methods->update(self, now);
    return self->anim_current ? self->anim_current : self;
}
