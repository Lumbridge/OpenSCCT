/* Placement callbacks for reflected property records and offline rendering objects. */
struct object_header {
    const void *methods;
};

struct dynamic_array {
    void *data;
    int count;
    int capacity;
};

static __inline void empty_array(struct dynamic_array *array)
{
    array->data = 0;
    array->count = 0;
    array->capacity = 0;
}

struct property_header {
    struct object_header object;
    unsigned char prefix[0x28 - sizeof(struct object_header)];
    void *field_28;
    unsigned char gap[4];
    unsigned short array_dimension;
    unsigned char property_fields[0x4c - 0x32];
    void *field_4c;
};

#define PLACE_PROPERTY(type) \
    extern const void *const vtable_##type[]; \
    void place_##type(struct property_header *property) \
    { \
        if (property) { \
            property->field_28 = 0; \
            property->array_dimension = 1; \
            property->field_4c = 0; \
            property->object.methods = vtable_##type; \
        } \
    }

struct vertex_stream {
    struct object_header object;
    unsigned char prefix[0x2c - sizeof(struct object_header)];
    int stride;
    int field_30;
    int format;
    struct dynamic_array vertices;
};

#define PLACE_VERTEX_STREAM(type, vertex_stride, format_id) \
    extern const void *const vtable_##type[]; \
    void place_##type(struct vertex_stream *stream) \
    { \
        if (stream) { \
            stream->stride = vertex_stride; \
            stream->field_30 = 0; \
            stream->format = format_id; \
            stream->object.methods = vtable_##type; \
            empty_array(&stream->vertices); \
        } \
    }

struct particle_emitter {
    struct object_header object;
    unsigned char prefix[0xc0 - sizeof(struct object_header)];
    float field_c0;
    unsigned char gap[8];
    unsigned int field_cc;
};

static __inline void initialize_particle_fields(struct particle_emitter *emitter)
{
    emitter->field_c0 = 1.0f;
    emitter->field_cc = 0;
}

#define PLACE_PARTICLE(type) \
    extern const void *const vtable_##type[]; \
    void place_##type(struct particle_emitter *emitter) \
    { \
        if (emitter) { \
            initialize_particle_fields(emitter); \
            emitter->object.methods = vtable_##type; \
        } \
    }

extern const void *const vtable_UParticleEmitter[];
void place_UParticleEmitter(struct particle_emitter *emitter)
{
    if (emitter) {
        emitter->object.methods = vtable_UParticleEmitter;
        initialize_particle_fields(emitter);
    }
}

struct value_record {
    struct object_header object;
    unsigned char prefix[0x30 - sizeof(struct object_header)];
    struct dynamic_array value;
};

struct buffer_record {
    struct object_header object;
    unsigned char prefix[0x2c - sizeof(struct object_header)];
    struct dynamic_array buffer;
};

#define PLACE_ARRAY(type, record, member) \
    extern const void *const vtable_##type[]; \
    void place_##type(struct record *storage) \
    { \
        if (storage) { \
            storage->object.methods = vtable_##type; \
            empty_array(&storage->member); \
        } \
    }

struct mesh_animation {
    struct object_header object;
    unsigned char prefix[0x2c - sizeof(struct object_header)];
    struct dynamic_array arrays[3];
};

extern const void *const vtable_UMeshAnimation[];
void place_UMeshAnimation(struct mesh_animation *animation)
{
    if (animation) {
        animation->object.methods = vtable_UMeshAnimation;
        empty_array(&animation->arrays[0]);
        empty_array(&animation->arrays[1]);
        empty_array(&animation->arrays[2]);
    }
}

struct text_buffer_factory {
    struct object_header object;
    unsigned char prefix[0x30 - sizeof(struct object_header)];
    struct dynamic_array arrays[3];
};

extern const void *const vtable_UTextBufferFactory[];
void place_UTextBufferFactory(struct text_buffer_factory *factory)
{
    if (factory) {
        empty_array(&factory->arrays[0]);
        empty_array(&factory->arrays[1]);
        empty_array(&factory->arrays[2]);
        factory->object.methods = vtable_UTextBufferFactory;
    }
}

struct vector3 { float x, y, z; };
struct primitive {
    struct object_header object;
    unsigned char prefix[0x28 - sizeof(struct object_header)];
    struct vector3 minimum, maximum;
    struct vector3 sphere_center;
    float sphere_radius;
};

extern const struct vector3 *default_minimum;
extern const struct vector3 *default_maximum;
extern const void *const vtable_UPrimitive[];
static __forceinline void copy_vector(struct vector3 *destination, const struct vector3 *value)
{
    destination->x = value->x;
    destination->y = value->y;
    destination->z = value->z;
}

static __forceinline void initialize_primitive(struct primitive *primitive)
{
    primitive->object.methods = vtable_UPrimitive;
    copy_vector(&primitive->minimum, default_minimum);
    copy_vector(&primitive->maximum, default_maximum);
    primitive->sphere_center.x = 0.0f;
    primitive->sphere_center.y = 0.0f;
    primitive->sphere_center.z = 0.0f;
    primitive->sphere_radius = 0.0f;
}

void place_UPrimitive(struct primitive *primitive)
{
    if (primitive)
        initialize_primitive(primitive);
}

#define PLACE_PRIMITIVE(type) \
    extern const void *const vtable_##type[]; \
    void place_##type(struct primitive *primitive) \
    { \
        if (primitive) { \
            initialize_primitive(primitive); \
            primitive->object.methods = vtable_##type; \
        } \
    }

PLACE_PRIMITIVE(UMeshInstance)
PLACE_PRIMITIVE(ULodMeshInstance)
PLACE_PRIMITIVE(UProjectorPrimitive)
PLACE_ARRAY(UEnum, value_record, value)
PLACE_ARRAY(UConst, value_record, value)
PLACE_ARRAY(UIndexBuffer, buffer_record, buffer)
PLACE_ARRAY(USkinVertexBuffer, buffer_record, buffer)
PLACE_VERTEX_STREAM(UVertexStreamVECTOR, 12, 1)
PLACE_VERTEX_STREAM(UVertexStreamUV, 8, 3)
PLACE_VERTEX_STREAM(UVertexStreamPosNormTex, 40, 5)
PLACE_VERTEX_STREAM(UVertexBuffer, 44, 4)

PLACE_PROPERTY(UByteProperty)
PLACE_PROPERTY(UIntProperty)
PLACE_PROPERTY(UDelegateProperty)
PLACE_PROPERTY(UBoolProperty)
PLACE_PROPERTY(UFloatProperty)
PLACE_PROPERTY(UObjectProperty)
PLACE_PROPERTY(UClassProperty)
PLACE_PROPERTY(UNameProperty)
PLACE_PROPERTY(UStrProperty)
PLACE_PROPERTY(UFixedArrayProperty)
PLACE_PROPERTY(UArrayProperty)
PLACE_PROPERTY(UMapProperty)
PLACE_PROPERTY(UStructProperty)
PLACE_PARTICLE(UBeamEmitter)
PLACE_PARTICLE(UMeshEmitter)
PLACE_PARTICLE(USparkEmitter)
PLACE_PARTICLE(USpriteEmitter)
PLACE_PARTICLE(UTrailEmitter)
PLACE_PARTICLE(UFlicFlocEmitter)
