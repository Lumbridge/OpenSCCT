struct material;
struct texture_interface;

struct material_fields {
    unsigned char prefix[0x3c];
    struct material *fallback;
    struct material *default_material;
    unsigned int use_fallback : 1;
    unsigned int validated : 1;
};

struct bitmap_material {
    unsigned char prefix[0x4c];
    int width, height;
};

struct texture {
    unsigned char prefix[0x6c];
    struct texture_interface *render_interface;
    unsigned char before_flags[0x44];
    unsigned int masked : 1;
    unsigned int alpha_texture : 1;
    unsigned int two_sided : 1;
    unsigned int realtime : 1;
    unsigned int parametric : 1;
    unsigned int realtime_changed : 1;
};

struct proxy_bitmap_material {
    unsigned char prefix[0x64];
    struct texture_interface *render_interface;
};

struct constant_color {
    unsigned char prefix[0x4c];
    unsigned int color;
};

int __fastcall material_get_validated(const struct material_fields *self)
{
    return self->validated;
}

void __fastcall material_set_validated(struct material_fields *self, void *unused, int validated)
{
    self->validated = validated;
}

int __fastcall material_has_fallback(const struct material_fields *self)
{
    return self->fallback != 0;
}

int __fastcall bitmap_material_width(const struct bitmap_material *self)
{
    return self->width;
}

int __fastcall bitmap_material_height(const struct bitmap_material *self)
{
    return self->height;
}

void __fastcall texture_mark_realtime_changed(struct texture *self)
{
    self->realtime_changed = 1;
}

int __fastcall texture_has_alpha(const struct texture *self)
{
    return self->alpha_texture;
}

struct texture_interface *__fastcall texture_render_interface(const struct texture *self)
{
    return self->render_interface;
}

struct texture_interface *__fastcall proxy_bitmap_render_interface(const struct proxy_bitmap_material *self)
{
    return self->render_interface;
}

unsigned int *__fastcall constant_material_get_color(const struct constant_color *self, void *unused,
                                                     unsigned int *result, float time_seconds)
{
    *result = self->color;
    return result;
}
