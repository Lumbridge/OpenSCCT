struct Channel {
    unsigned sequence;
    unsigned char reserved_04[0x24];
    unsigned flags;
    unsigned char reserved_2c[0x54];
};
struct Mesh {
    unsigned char reserved[0x94];
    struct Channel *channels;
    int count;
};
struct Index { int value; };
struct Output { unsigned *name; };
static __forceinline int valid(const struct Mesh *mesh, int i)
{
    return mesh->count > i && i >= 0;
}
int __fastcall channel_flag_one(const struct Mesh *mesh, struct Index index)
{
    return valid(mesh, index.value) ? (mesh->channels[index.value].flags >> 1) & 1u : 0;
}
int __fastcall channel_flag_four(const struct Mesh *mesh, struct Index index)
{
    return valid(mesh, index.value) ? (mesh->channels[index.value].flags >> 4) & 1u : 0;
}
int __fastcall clear_channel_flags(struct Mesh *mesh, struct Index index)
{
    if (valid(mesh, index.value)) {
        mesh->channels[index.value].flags &= ~2u;
        mesh->channels[index.value].flags &= ~1u;
        return 1;
    }
    return 0;
}
unsigned *__fastcall channel_sequence_name(const struct Mesh *mesh, struct Output output, struct Index index)
{
    if (index.value >= 0 && index.value < mesh->count) {
        *output.name = mesh->channels[index.value].sequence;
        return output.name;
    }
    *output.name = 0;
    return output.name;
}
