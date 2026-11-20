struct Channel {
    unsigned char reserved_00[8];
    float rate;
    float position;
    float end_position;
    unsigned char reserved_14[4];
    float field_18;
    unsigned char reserved_1c[20];
    float field_30;
    unsigned char reserved_34[12];
    float field_40;
    unsigned char reserved_44[60];
};
struct Mesh { unsigned char reserved[0x94]; struct Channel *channels; int count; };
struct Index { int value; };
extern const float zero_value;

#define CHANNEL_QUERY(Entry, Field) \
    float __fastcall Entry(const struct Mesh *mesh, struct Index index) \
    { \
        if (index.value >= 0 && index.value < mesh->count) \
            return mesh->channels[index.value].Field; \
        return zero_value; \
    }

CHANNEL_QUERY(query_0d5280, end_position)
CHANNEL_QUERY(query_0d52e0, rate)
CHANNEL_QUERY(query_0d5380, field_30)
CHANNEL_QUERY(query_0d53b0, field_40)
CHANNEL_QUERY(query_0d5540, position)
CHANNEL_QUERY(query_0d5570, end_position)
CHANNEL_QUERY(query_0d55a0, field_18)
