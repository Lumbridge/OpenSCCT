struct vector3 { float x, y, z; };
struct box { struct vector3 minimum, maximum; };
struct sphere { struct vector3 center; float radius; };
struct primitive_bounds {
    unsigned char prefix[0x28];
    struct box box;
    struct sphere sphere;
};

struct actor_placement {
    unsigned char prefix[0x80];
    struct vector3 location;
};
struct mesh;

struct box *__fastcall primitive_box_bounds(const struct primitive_bounds *self, void *unused,
                                             struct box *result, const struct actor_placement *owner)
{
    result->minimum.x = self->box.minimum.x;
    result->minimum.y = self->box.minimum.y;
    result->minimum.z = self->box.minimum.z;
    result->maximum.x = self->box.maximum.x;
    result->maximum.y = self->box.maximum.y;
    result->maximum.z = self->box.maximum.z;
    return result;
}

struct sphere *__fastcall primitive_sphere_bounds(const struct primitive_bounds *self, void *unused,
                                                   struct sphere *result, const struct actor_placement *owner)
{
    result->center.x = self->sphere.center.x;
    result->center.y = self->sphere.center.y;
    result->center.z = self->sphere.center.z;
    result->radius = self->sphere.radius;
    return result;
}

struct vector3 *__fastcall mesh_owner_location(const struct mesh *self, void *unused,
                                               struct vector3 *result, const struct actor_placement *owner)
{
    result->x = owner->location.x;
    result->y = owner->location.y;
    result->z = owner->location.z;
    return result;
}
