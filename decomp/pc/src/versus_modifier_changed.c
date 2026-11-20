struct ObjectClass {
    unsigned char before_base[0x28];
    struct ObjectClass *base;
};
struct EngineObject {
    unsigned char before_outer[0x18];
    struct EngineObject *outer;
    unsigned char before_class[8];
    struct ObjectClass *type;
    unsigned char before_changed[8];
    int changed;
};
#define OUTER_CLASS ((struct ObjectClass *)0x10cca798u)
static __forceinline void mark_outer_changed(struct EngineObject *object) {
    struct ObjectClass *type;
    while (object->outer) {
        object = object->outer;
    }
    for (type = object->type; type; type = type->base) {
        if (type == OUTER_CLASS) {
            object->changed = 1;
            return;
        }
    }
}
__declspec(noinline) void __fastcall versus_modifier_changed(struct EngineObject *self) {
    mark_outer_changed(self);
}
void __fastcall versus_final_blend_changed(struct EngineObject *self) {
    versus_modifier_changed(self);
    mark_outer_changed(self);
}
