struct Index { int value; };
struct Bit { unsigned value; };
union Flags {
    unsigned value;
    struct { unsigned lower : 6, sixth : 1, upper : 25; } bits;
};
struct Channel {
    unsigned char reserved_00[8];
    float rate, position, end_position, blend_rate, field_18;
    unsigned char reserved_1c[8];
    float rate_scale;
    union Flags flags;
    unsigned reserved_2c;
    float weight;
    unsigned char reserved_34[8];
    float field_3c;
    unsigned char reserved_40[0x40];
};
struct Mesh {
    unsigned char reserved_00[0x94];
    struct Channel *channels;
    int count;
    unsigned char reserved_9c[0x248];
    float step;
};
extern const float zero_value;
extern const double zero_position;
static __forceinline int valid(const struct Mesh *mesh, int index)
{
    return index >= 0 && index < mesh->count;
}
void __fastcall update_position(struct Mesh *mesh, struct Index index, float position, struct Bit bit)
{
    if (valid(mesh, index.value)) {
        struct Channel *channel = &mesh->channels[index.value];
        if (channel->position < zero_position) {
            if (position < zero_value)
                channel->position = position;
            else
                channel->field_18 = position;
        } else {
            channel->position = position;
        }
        channel->flags.bits.sixth = bit.value;
    }
}
void __fastcall approach_weight(struct Mesh *mesh, struct Index index, float target, float step)
{
    if (valid(mesh, index.value)) {
        struct Channel *channel = &mesh->channels[index.value];
        float delta = channel->weight - target;
        if ((delta >= zero_value ? delta : -delta) <= step)
            channel->weight = target;
        else if (target > channel->weight)
            channel->weight += step;
        else
            channel->weight -= step;
    }
}
