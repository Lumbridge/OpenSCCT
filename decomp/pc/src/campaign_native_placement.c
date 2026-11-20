/* MSVC x86 passes the float word on the stack. Its positive zero supplies
   the null superclass pointer for UField's thiscall constructor. */
struct RuntimeProperty {
    const void *const *methods;
    unsigned char field_prefix[0x34 - 4];
    unsigned short array_dim;
    unsigned char property_fields[0x50 - 0x36];
    struct RuntimeProperty *next_property;
};
extern void __fastcall initialize_field(struct RuntimeProperty *, float null_super);
#define PLACE_PROPERTY(Type) \
    extern const void *const vtable_##Type[]; \
    void place_##Type(struct RuntimeProperty *property) \
    { \
        if (property) { \
            initialize_field(property, 0.0f); \
            property->array_dim = 1; \
            property->next_property = 0; \
            property->methods = vtable_##Type; \
        } \
    }

struct VertexStream {
    const void *const *methods;
    unsigned char prefix[0x2c - 4];
    int stride, first_vertex, format;
    void *data;
    int count, capacity;
};
void __fastcall initialize_object(void *object);
#define PLACE_STREAM(Type, Stride, Format) \
    extern const void *const vtable_##Type[]; \
    void place_##Type(struct VertexStream *stream) \
    { \
        if (stream) { \
            initialize_object(stream); \
            stream->stride = Stride; \
            stream->first_vertex = 0; \
            stream->format = Format; \
            stream->methods = vtable_##Type; \
            stream->data = 0; \
            stream->count = 0; \
            stream->capacity = 0; \
        } \
    }

struct NativeActor {
    const void *const *methods;
    unsigned char prefix[0x1cc - 4];
    float initial_values[4];
};
struct ConstraintActor {
    struct NativeActor actor;
    unsigned char fields[0x2b4 - 0x1dc];
    void *constraint;
};
static __forceinline void set_actor_values(struct NativeActor *actor)
{
    actor->initial_values[0] = 0.0f;
    actor->initial_values[1] = 0.0f;
    actor->initial_values[2] = 1.0e-8f;
    actor->initial_values[3] = 0.0f;
}
#define PLACE_CONSTRAINT(Type, Derived) \
    extern const void *const vtable_##Type[]; \
    void place_##Type(struct ConstraintActor *object) \
    { \
        if (object) { \
            initialize_object(object); \
            set_actor_values(&object->actor); \
            if (Derived) object->constraint = 0; \
            object->actor.methods = vtable_##Type; \
            if (!Derived) object->constraint = 0; \
        } \
    }
extern const void *const vtable_AActor[];
void place_AActor(struct NativeActor *actor)
{
    if (actor) {
        initialize_object(actor);
        actor->methods = vtable_AActor;
        set_actor_values(actor);
    }
}

PLACE_CONSTRAINT(AEHkBallAndSocketConstraint, 1)
PLACE_CONSTRAINT(AEHkConstraint, 0)
PLACE_CONSTRAINT(AEHkHingeConstraint, 1)
PLACE_CONSTRAINT(AEHkMotor, 0)
PLACE_CONSTRAINT(AEHkPrismaticConstraint, 1)
PLACE_CONSTRAINT(AEHkStiffSpringConstraint, 1)
PLACE_PROPERTY(UArrayProperty)
PLACE_PROPERTY(UBoolProperty)
PLACE_PROPERTY(UByteProperty)
PLACE_PROPERTY(UClassProperty)
PLACE_PROPERTY(UFixedArrayProperty)
PLACE_PROPERTY(UFloatProperty)
PLACE_PROPERTY(UIntProperty)
PLACE_PROPERTY(UMapProperty)
PLACE_PROPERTY(UNameProperty)
PLACE_PROPERTY(UObjectProperty)
PLACE_PROPERTY(UStrProperty)
PLACE_PROPERTY(UStructProperty)
PLACE_STREAM(UVertexBuffer, 44, 4)
PLACE_STREAM(UVertexStreamCOLOR, 4, 2)
PLACE_STREAM(UVertexStreamPosNormTex, 40, 5)
PLACE_STREAM(UVertexStreamUV, 8, 3)
PLACE_STREAM(UVertexStreamVECTOR, 12, 1)
