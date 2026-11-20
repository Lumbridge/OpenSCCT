struct Material;
/* Slot 31 is an observed Boolean query whose original name is unconfirmed. */
struct MaterialMethods {
    void *before_size[26];
    int (__fastcall *u_size)(struct Material *);
    int (__fastcall *v_size)(struct Material *);
    void *before_uv_streams[2];
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
    float detail_scale;
    unsigned char output_blending;
};
struct LitSpecular {
    struct Material base;
    struct Material *diffuse, *specular, *specularity_mask;
};
struct Modifier {
    struct Material base;
    struct Material *material;
};
struct TextureModifier {
    struct Modifier base;
    unsigned char coordinate_source;
};

struct SequenceItem {
    struct Material *material;
    float time;
    int action;
};
struct MaterialSequence {
    struct Modifier base;
    struct SequenceItem *items;
    int count, capacity;
    unsigned char before_current[8];
    float current_time, last_time, total_time;
};
struct MaterialSwitch {
    struct Modifier base;
    int current;
    struct Material **materials;
    int count;
};
struct TextureOscillator {
    struct TextureModifier base;
    unsigned char before_last_u[0xbc - sizeof(struct TextureModifier)];
    float last_u, last_v;
};
struct TextureEnvironment {
    struct TextureModifier base;
    unsigned char before_map_type[0x58 - sizeof(struct TextureModifier)];
    unsigned char map_type;
};
struct TextureStream {
    struct TextureModifier base;
    unsigned char before_stream[0x58 - sizeof(struct TextureModifier)];
    int stream;
};
extern void __fastcall versus_base_modifier_changed(struct Modifier *self);

int __fastcall versus_shader_blend_query(struct Shader *self) {
    enum OutputBlending { Normal, Masked, Modulate, Translucent, Invisible, Brighten, Darken };
    return (self->output_blending == Normal && self->opacity) ||
           self->output_blending == Modulate || self->output_blending == Brighten ||
           self->output_blending == Darken || self->output_blending == Translucent;
}
int __fastcall versus_lit_specular_has_fallback(struct LitSpecular *self) {
    return self->base.fallback || self->diffuse;
}

int __fastcall versus_sequence_u_size(struct MaterialSequence *self) {
    int i;
    for (i = 0; i < self->count; ++i) {
        if (self->items[i].material) {
            struct Material *item = self->items[i].material;
            return item->methods->u_size(item);
        }
    }
    return 0;
}
int __fastcall versus_tex_modifier_u_size(struct TextureModifier *self) {
    struct Material *source = self->base.material;
    int size = 0;
    if (source) {
        size = source->methods->u_size(source);
    }
    return size;
}

int __fastcall versus_sequence_v_size(struct MaterialSequence *self) {
    int i;
    for (i = 0; i < self->count; ++i) {
        if (self->items[i].material) {
            struct Material *item = self->items[i].material;
            return item->methods->v_size(item);
        }
    }
    return 0;
}
int __fastcall versus_tex_modifier_v_size(struct TextureModifier *self) {
    struct Material *source = self->base.material;
    int size = 0;
    if (source) {
        size = source->methods->v_size(source);
    }
    return size;
}

unsigned char __fastcall versus_lit_specular_uv_streams(struct LitSpecular *self) {
    struct Material *material;
    unsigned char streams = 1;
    material = self->diffuse;
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
    return streams;
}

int __fastcall versus_lit_specular_any_slot31(struct LitSpecular *self) {
    struct Material *material;
    material = self->diffuse;
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
    return 0;
}

void __fastcall versus_sequence_changed(struct MaterialSequence *self) {
    int i;
    versus_base_modifier_changed(&self->base);
    self->current_time = 0;
    self->last_time = 0;
    self->total_time = 0;
    for (i = 0; i < self->count; ++i) {
        self->total_time += self->items[i].time;
    }
}
void __fastcall versus_switch_changed(struct MaterialSwitch *self) {
    versus_base_modifier_changed(&self->base);
    self->base.material = self->current >= 0 && self->current < self->count
                          ? self->materials[self->current] : 0;
}
void __fastcall versus_oscillator_changed(struct TextureOscillator *self) {
    versus_base_modifier_changed(&self->base.base);
    self->last_v = 0;
    self->last_u = 0;
}
void __fastcall versus_stream_changed(struct TextureStream *self) {
    int value;
    versus_base_modifier_changed(&self->base.base);
    value = self->stream;
    self->stream = value < 0 ? 0 : value < 7 ? value : 7;
    self->base.coordinate_source = (unsigned char)self->stream;
}
enum EnvironmentType { WorldSpace, CameraSpace };
enum EnvironmentCoords { WorldEnvMapCoords = 10, CameraEnvMapCoords = 11 };
struct Matrix;
struct Matrix *__fastcall versus_environment_matrix(struct TextureEnvironment *self, float seconds) {
    switch (self->map_type) {
    case WorldSpace:
        self->base.coordinate_source = WorldEnvMapCoords;
        break;
    case CameraSpace:
        self->base.coordinate_source = CameraEnvMapCoords;
        break;
    }
    return 0;
}
