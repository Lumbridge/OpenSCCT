struct Material;
struct History {
    struct Material **items;
    int count, capacity;
};
struct HistoryArg { struct History *history; };
struct IntArg { int value; };
struct Methods {
    void *before_cycle[22];
    int (__fastcall *check_cycle)(struct Material *, struct HistoryArg);
};
struct Material {
    const struct Methods *methods;
    unsigned char unknown[0x38];
    struct Material *fallback;
    unsigned char before_layers[0x0c];
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
struct SequenceItem { struct Material *material; float time; int action; };
struct Sequence {
    struct Modifier base;
    struct SequenceItem *items;
    int count;
};
struct Switch {
    struct Modifier base;
    int current;
    struct Material **items;
    int count;
};
struct EMFChannels {
    struct Material base;
    struct Material **items;
    int count;
};
extern int __fastcall versus_history_add(struct History *, __int64 count_and_size);
extern void __fastcall versus_history_remove(struct History *, struct IntArg index, __int64 count_and_size);
/* Explicit adjacent 32-bit stack arguments occupy one 64-bit argument slot. */
static __forceinline __int64 array_extent(unsigned count, unsigned size) {
    return ((__int64)size << 32) | count;
}
static __forceinline struct IntArg stack_word(int value) {
    struct IntArg arg;
    arg.value = value;
    return arg;
}
static __forceinline int find_material(struct History *history, struct Material *material) {
    int i;
    for (i = 0; i < history->count; ++i) {
        if (history->items[i] == material) {
            return i;
        }
    }
    return -1;
}
static __forceinline int append_material(struct History *history, struct Material *material) {
    int index = versus_history_add(history, array_extent(1, sizeof(material)));
    history->items[index] = material;
    return index;
}
static __forceinline void remove_material(struct History *history, int index) {
    versus_history_remove(history, stack_word(index), array_extent(1, sizeof(struct Material *)));
}
static __forceinline struct HistoryArg history_arg(struct History *history) {
    struct HistoryArg arg;
    arg.history = history;
    return arg;
}
static __forceinline int child_is_acyclic(struct Material *material, struct History *history) {
    return !material || material->methods->check_cycle(material, history_arg(history));
}
__declspec(noinline) int __fastcall versus_material_check_cycle(struct Material *self, struct HistoryArg arg) {
    struct History *history = arg.history;
    if (find_material(history, self) != -1) {
        return 0;
    }
    if (self->fallback) {
        int i = append_material(history, self);
        if (!self->fallback->methods->check_cycle(self->fallback, history_arg(history))) {
            return 0;
        }
        remove_material(history, i);
    }
    return 1;
}

__declspec(noinline) int __fastcall versus_shader_check_cycle(struct Shader *self, struct HistoryArg arg) {
    struct History *history = arg.history;
    int mark;
    if (!versus_material_check_cycle(&self->base, arg)) {
        return 0;
    }
    mark = append_material(history, (struct Material *)self);
    if (!child_is_acyclic(self->diffuse, history)) {
        goto found_cycle;
    }
    if (!child_is_acyclic(self->opacity, history)) {
        goto found_cycle;
    }
    if (!child_is_acyclic(self->specular, history)) {
        goto found_cycle;
    }
    if (!child_is_acyclic(self->specularity_mask, history)) {
        goto found_cycle;
    }
    if (!child_is_acyclic(self->self_illumination, history)) {
        goto found_cycle;
    }
    if (!child_is_acyclic(self->illumination_mask, history)) {
        goto found_cycle;
    }
    if (!child_is_acyclic(self->detail, history)) {
        goto found_cycle;
    }
    remove_material(history, mark);
    return 1;
found_cycle:
    return 0;
}

__declspec(noinline) int __fastcall versus_lit_specular_check_cycle(struct LitSpecular *self, struct HistoryArg arg) {
    struct History *history = arg.history;
    int mark;
    if (!versus_material_check_cycle(&self->base, arg)) {
        return 0;
    }
    mark = append_material(history, (struct Material *)self);
    if (!child_is_acyclic(self->diffuse, history)) {
        goto found_cycle;
    }
    if (!child_is_acyclic(self->specular, history)) {
        goto found_cycle;
    }
    if (!child_is_acyclic(self->specularity_mask, history)) {
        goto found_cycle;
    }
    remove_material(history, mark);
    return 1;
found_cycle:
    return 0;
}

__declspec(noinline) int __fastcall versus_combiner_check_cycle(struct Combiner *self, struct HistoryArg arg) {
    struct History *history = arg.history;
    int mark;
    if (!versus_material_check_cycle(&self->base, arg)) {
        return 0;
    }
    mark = append_material(history, (struct Material *)self);
    if (!child_is_acyclic(self->material1, history)) {
        goto found_cycle;
    }
    if (!child_is_acyclic(self->material2, history)) {
        goto found_cycle;
    }
    if (!child_is_acyclic(self->mask, history)) {
        goto found_cycle;
    }
    remove_material(history, mark);
    return 1;
found_cycle:
    return 0;
}

__declspec(noinline) int __fastcall versus_modifier_check_cycle(struct Modifier *self, struct HistoryArg arg) {
    struct History *history = arg.history;
    int mark;
    if (!versus_material_check_cycle(&self->base, arg)) {
        return 0;
    }
    mark = append_material(history, (struct Material *)self);
    if (!child_is_acyclic(self->material, history)) {
        return 0;
    }
    remove_material(history, mark);
    return 1;
}

__declspec(noinline) int __fastcall versus_emf_check_cycle(struct EMFChannels *self, struct HistoryArg arg) {
    struct History *history = arg.history;
    int mark;
    int i;
    if (!versus_material_check_cycle(&self->base, arg)) {
        return 0;
    }
    mark = append_material(history, (struct Material *)self);
    for (i = 0; i < self->count; ++i) {
        if (!child_is_acyclic(self->items[i], history)) {
            return 0;
        }
    }
    remove_material(history, mark);
    return 1;
}

__declspec(noinline) int __fastcall versus_switch_check_cycle(struct Switch *self, struct HistoryArg arg) {
    struct History *history = arg.history;
    int mark;
    int i;
    if (!versus_modifier_check_cycle(&self->base, arg)) {
        return 0;
    }
    mark = append_material(history, (struct Material *)self);
    for (i = 0; i < self->count; ++i) {
        if (!child_is_acyclic(self->items[i], history)) {
            return 0;
        }
    }
    remove_material(history, mark);
    return 1;
}

__declspec(noinline) int __fastcall versus_sequence_check_cycle(struct Sequence *self, struct HistoryArg arg) {
    struct History *history = arg.history;
    int mark;
    int i;
    if (!versus_modifier_check_cycle(&self->base, arg)) {
        return 0;
    }
    mark = append_material(history, (struct Material *)self);
    for (i = 0; i < self->count; ++i) {
        if (!child_is_acyclic(self->items[i].material, history)) {
            return 0;
        }
    }
    remove_material(history, mark);
    return 1;
}
