struct Material;
/* The validated value is an explicit stack argument to an x86 member call. */
struct StackInt { int value; };
/* The original names of slots 28, 29 and 31 are unconfirmed. */
struct MaterialMethods {
    void *before_validated[23];
    int (__fastcall *get_validated)(struct Material *);
    void (__fastcall *set_validated)(struct Material *, struct StackInt value);
    void (__fastcall *pre_set)(struct Material *, float seconds);
    int (__fastcall *u_size)(struct Material *);
    int (__fastcall *v_size)(struct Material *);
    int (__fastcall *query28)(struct Material *);
    int (__fastcall *query29)(struct Material *);
    unsigned char (__fastcall *uv_streams)(struct Material *);
    int (__fastcall *query31)(struct Material *);
    void *unknown32;
    struct Material *(__fastcall *check_fallback)(struct Material *);
};
struct Material {
    const struct MaterialMethods *methods;
    unsigned char before_fallback[0x38];
    struct Material *fallback;
    struct Material *default_material;
    unsigned flags;
    unsigned char before_inputs[4];
};
struct Shader {
    struct Material base;
    struct Material *diffuse, *opacity, *specular, *specularity_mask;
    struct Material *self_illumination, *illumination_mask, *detail;
};
struct LitSpecular {
    struct Material base;
    struct Material *diffuse, *specular, *specularity_mask;
};
struct Combiner {
    struct Material base;
    unsigned char operations[4];
    struct Material *material1, *material2, *mask;
};
struct Modifier {
    struct Material base;
    struct Material *material;
};
struct TextureModifier {
    struct Modifier base;
    unsigned char coordinate_source;
};
enum FrameBufferBlending {
    FB_Overwrite, FB_Modulate, FB_AlphaBlend, FB_AlphaModulate,
    FB_Translucent, FB_Darken, FB_Brighten
};
struct FinalBlend {
    struct Modifier base;
    unsigned char blending;
};
struct EMFChannelTextures {
    struct Material base;
    struct Material **channels;
    int count;
};
static __inline int child_u_size(struct Material *child) {
    return child ? child->methods->u_size(child) : 0;
}
static __inline int child_v_size(struct Material *child) {
    return child ? child->methods->v_size(child) : 0;
}
static __inline int child_query28(struct Material *child) {
    return child ? child->methods->query28(child) : 0;
}
static __inline int child_query29(struct Material *child) {
    return child ? child->methods->query29(child) : 0;
}
static __inline int child_get_validated(struct Material *child) {
    return child ? child->methods->get_validated(child) : 1;
}
static __inline void pre_set_child(struct Material *material, float seconds) {
    if (material) {
        struct Material *resolved = material->methods->check_fallback(material);
        resolved->methods->pre_set(resolved, seconds);
    }
}
struct Material *__fastcall versus_material_check_fallback(struct Material *self) {
    struct Material *fallback = self->fallback;
    if (fallback && (self->flags & 1)) {
        return fallback->methods->check_fallback(fallback);
    }
    return self;
}

void __fastcall versus_shader_pre_set(struct Shader *self, float seconds) {
    pre_set_child(self->diffuse, seconds);
    pre_set_child(self->opacity, seconds);
    pre_set_child(self->specular, seconds);
    pre_set_child(self->specularity_mask, seconds);
    pre_set_child(self->self_illumination, seconds);
    pre_set_child(self->illumination_mask, seconds);
    pre_set_child(self->detail, seconds);
}

void __fastcall versus_lit_specular_pre_set(struct LitSpecular *self, float seconds) {
    pre_set_child(self->diffuse, seconds);
    pre_set_child(self->specular, seconds);
    pre_set_child(self->specularity_mask, seconds);
}

void __fastcall versus_combiner_pre_set(struct Combiner *self, float seconds) {
    struct Material *mask;
    pre_set_child(self->material1, seconds);
    pre_set_child(self->material2, seconds);
    mask = self->mask;
    if (mask && mask != self->material1 && mask != self->material2) {
        struct Material *resolved = mask->methods->check_fallback(mask);
        resolved->methods->pre_set(resolved, seconds);
    }
}

void __fastcall versus_modifier_pre_set(struct Modifier *self, float seconds) {
    pre_set_child(self->material, seconds);
}

void __fastcall versus_emf_pre_set(struct EMFChannelTextures *self, float seconds) {
    int i;
    for (i = 0; i < self->count; ++i) {
        pre_set_child(self->channels[i], seconds);
    }
}
static __inline int default_streams(struct Material *material) {
    return material ? material->methods->uv_streams(material) : 1;
}
unsigned char __fastcall versus_modifier_uv_streams(struct Modifier *self) {
    return default_streams(self->material);
}
unsigned char __fastcall versus_combiner_uv_streams(struct Combiner *self) {
    int first = default_streams(self->material1);
    int second = default_streams(self->material2);
    return first | second;
}
unsigned char __fastcall versus_tex_modifier_uv_streams(struct TextureModifier *self) {
    if (self->coordinate_source <= 7) {
        return default_streams(self->base.material) | (1 << self->coordinate_source);
    }
    return default_streams(self->base.material);
}
unsigned char __fastcall versus_shader_uv_streams(struct Shader *self) {
    unsigned char streams = 1;
    struct Material *material;
    material = self->diffuse;
    if (material) {
        streams |= material->methods->uv_streams(material);
    }
    material = self->opacity;
    if (material) {
        streams |= material->methods->uv_streams(material);
    }
    material = self->specular;
    if (material) {
        streams |= material->methods->uv_streams(material);
    }
    material = self->specularity_mask;
    if (material) {
        streams |= material->methods->uv_streams(material);
    }
    material = self->self_illumination;
    if (material) {
        streams |= material->methods->uv_streams(material);
    }
    material = self->illumination_mask;
    if (material) {
        streams |= material->methods->uv_streams(material);
    }
    material = self->detail;
    if (material) {
        streams |= material->methods->uv_streams(material);
    }
    return streams;
}

int __fastcall versus_shader_any_slot31(struct Shader *self) {
    struct Material *material;
    material = self->diffuse;
    if (material && material->methods->query31(material)) {
        return 1;
    }
    material = self->opacity;
    if (material && material->methods->query31(material)) {
        return 1;
    }
    material = self->specular;
    if (material && material->methods->query31(material)) {
        return 1;
    }
    material = self->specularity_mask;
    if (material && material->methods->query31(material)) {
        return 1;
    }
    material = self->self_illumination;
    if (material && material->methods->query31(material)) {
        return 1;
    }
    material = self->illumination_mask;
    if (material && material->methods->query31(material)) {
        return 1;
    }
    material = self->detail;
    if (material && material->methods->query31(material)) {
        return 1;
    }
    return 0;
}

int __fastcall versus_modifier_u_size(struct Modifier *self) {
    return child_u_size(self->material);
}

int __fastcall versus_modifier_v_size(struct Modifier *self) {
    return child_v_size(self->material);
}

int __fastcall versus_modifier_query28(struct Modifier *self) {
    return child_query28(self->material);
}

int __fastcall versus_modifier_query29(struct Modifier *self) {
    return child_query29(self->material);
}

int __fastcall versus_modifier_get_validated(struct Modifier *self) {
    return child_get_validated(self->material);
}

void __fastcall versus_modifier_set_validated(struct Modifier *self, struct StackInt value) {
    struct Material *material = self->material;
    if (material) {
        material->methods->set_validated(material, value);
    }
}
int __fastcall versus_final_blend_mode_query(struct FinalBlend *self) {
    return self->blending == FB_Modulate || self->blending == FB_AlphaBlend ||
           self->blending == FB_AlphaModulate || self->blending == FB_Translucent ||
           self->blending == FB_Darken || self->blending == FB_Brighten;
}

int __fastcall versus_combiner_u_size(struct Combiner *self) {
    int second = child_u_size(self->material2);
    int first = child_u_size(self->material1);
    return first >= second ? first : second;
}

int __fastcall versus_combiner_v_size(struct Combiner *self) {
    int second = child_v_size(self->material2);
    int first = child_v_size(self->material1);
    return first >= second ? first : second;
}
