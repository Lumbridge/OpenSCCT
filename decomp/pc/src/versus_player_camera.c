struct vector3 { float x, y, z; };
struct rotator { int pitch, yaw, roll; };
struct camera {
    unsigned char prefix[0x18c8];
    int debug_mode;
    unsigned char before_debug_transform[0xc];
    struct vector3 debug_position;
    struct rotator debug_rotation;
};
enum { pawn_walking = 0x04u, pawn_crouched = 0x20u };
struct pawn {
    unsigned char prefix[0x4a8];
    unsigned int movement_flags;
};
struct class_record {
    unsigned char prefix[0x28];
    const struct class_record *super;
};
struct player {
    unsigned char prefix[0x24];
    const struct class_record *type;
};
struct player_controller {
    unsigned char prefix[0x2fc];
    const struct pawn *pawn;
    unsigned char before_player[0x1a4];
    const struct player *player;
    unsigned char before_transform[0x184];
    struct vector3 camera_position;
    struct rotator camera_rotation;
    unsigned char before_camera[0x154];
    const struct camera *camera;
};

void __fastcall player_camera_transform(const struct player_controller *self, void *unused,
                                        struct vector3 *position, struct rotator *rotation)
{
    if (self->camera->debug_mode == 1) {
        *position = self->camera->debug_position;
        *rotation = self->camera->debug_rotation;
    } else {
        *position = self->camera_position;
        *rotation = self->camera_rotation;
    }
}

int __fastcall player_walking_or_crouched(const struct player_controller *self)
{
    return self->pawn && (self->pawn->movement_flags & (pawn_walking | pawn_crouched));
}

int __fastcall player_uses_viewport(const struct player_controller *self)
{
    const struct class_record *type;
    if (self->player) {
        for (type = self->player->type; type; type = type->super)
            if (type == (const struct class_record *)0x10cbc930u)
                return 1;
    }
    return 0;
}
