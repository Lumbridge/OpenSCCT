struct vector3 { float x, y, z; };
struct rotator { int pitch, yaw, roll; };

struct actor_fields {
    unsigned char prefix[0x80];
    struct vector3 location;
    unsigned char before_rotation[0x54];
    struct rotator rotation;
    unsigned char before_light_radius[0x10];
    float light_radius;
    unsigned char before_collision[0x168];
    float collision_radius, collision_height;
    unsigned char before_flags[0x78];
    unsigned int flags;
    unsigned int flags_2ec;
    unsigned int collision_flags;
};
struct mover_fields {
    struct actor_fields actor;
    unsigned char before_base_position[0x88];
    struct vector3 base_position;
    unsigned char before_base_rotation[0x84];
    struct rotator base_rotation;
};
struct static_mesh;

/* Stock Steam 252084 pooled floats. */
#define radius_bias (*(const float *)0x10c109fcu)
#define light_unit_scale (*(const float *)0x10c10a58u)

void __fastcall actor_get_transform(const struct actor_fields *self, void *unused,
                                    struct vector3 *location, struct rotator *rotation)
{
    *location = self->location;
    *rotation = self->rotation;
}

int __fastcall actor_pending_delete(const struct actor_fields *self)
{
    return (self->flags >> 15) & 1u;
}

float __fastcall actor_world_light_radius(const struct actor_fields *self)
{
    return (self->light_radius + radius_bias) * light_unit_scale;
}

int __fastcall static_mesh_uses_collision_cylinder(const struct static_mesh *self, void *unused,
                                                   const struct actor_fields *owner)
{
    return (owner->collision_flags >> 21) & 1u;
}

void __fastcall mover_set_base_transform(struct mover_fields *self)
{
    self->base_position = self->actor.location;
    self->base_rotation = self->actor.rotation;
}
