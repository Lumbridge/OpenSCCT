struct Material;
struct MaterialMethods {
    void *before_size[26];
    int (__fastcall *u_size)(struct Material *);
    int (__fastcall *v_size)(struct Material *);
    void *before_fallback[5];
    struct Material *(__fastcall *check_fallback)(struct Material *);
};
struct Material {
    const struct MaterialMethods *methods;
    unsigned char before_fallback[0x38];
    struct Material *fallback;
    struct Material *default_material;
    unsigned flags;
    unsigned char before_shader[4];
};
struct Shader {
    struct Material base;
    struct Material *diffuse, *opacity, *specular, *specularity_mask, *self_illumination;
};

int __fastcall versus_shader_u_size(struct Shader *self) {
    struct Material *diffuse = self->diffuse;
    struct Material *illumination;
    if (diffuse) {
        return diffuse->methods->u_size(diffuse);
    }
    illumination = self->self_illumination;
    if (illumination) {
        return illumination->methods->u_size(illumination);
    }
    return 0;
}

int __fastcall versus_shader_v_size(struct Shader *self) {
    struct Material *diffuse = self->diffuse;
    struct Material *illumination;
    if (diffuse) {
        return diffuse->methods->v_size(diffuse);
    }
    illumination = self->self_illumination;
    if (illumination) {
        return illumination->methods->v_size(illumination);
    }
    return 0;
}

struct LitSpecular { struct Material base; struct Material *diffuse; };
struct Material *__fastcall versus_lit_specular_fallback(struct LitSpecular *self) {
    struct Material *fallback, *diffuse;
    if (!(self->base.flags & 1)) {
        return &self->base;
    }
    fallback = self->base.fallback;
    if (fallback) {
        return fallback->methods->check_fallback(fallback);
    }
    diffuse = self->diffuse;
    if (diffuse) {
        return diffuse->methods->check_fallback(diffuse);
    }
    return 0;
}
