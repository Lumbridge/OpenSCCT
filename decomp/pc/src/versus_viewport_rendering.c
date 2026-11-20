struct Viewport;
struct PointerArg { void *value; };
struct Device;
struct DeviceMethods {
    void *before_begin[36];
    void (__fastcall *begin)(struct Device *, void *reserved_edx, void *surface);
    void (__fastcall *end)(struct Device *, struct PointerArg viewport);
};
struct Device { const struct DeviceMethods *methods; };
struct Viewport {
    unsigned char before_device[0x70];
    struct Device *device;
    unsigned char before_size[0x94-0x74];
    int width, height;
    void *screen;
    unsigned char before_frame[0x130-0xa0];
    int frame_width, frame_height;
    unsigned char before_surface[8];
    void *surface;
    unsigned char before_active[12];
    int active;
};
static __inline struct PointerArg pointer_arg(void *value) {
    struct PointerArg arg;
    arg.value = value;
    return arg;
}
void __fastcall versus_viewport_begin(struct Viewport *self) {
    /* The original virtual call stages the stack argument in EDX. */
    self->device->methods->begin(self->device, self->surface, self->surface);
    self->active = 1;
}
void __fastcall versus_viewport_end(struct Viewport *self) {
    if (self->active) {
        self->active = 0;
        self->device->methods->end(self->device, pointer_arg(self));
        self->width = self->frame_width;
        self->height = self->frame_height;
        self->screen = 0;
    }
}
