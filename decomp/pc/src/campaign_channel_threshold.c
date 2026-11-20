struct Channel {
    unsigned char reserved_00[12];
    float position;
    float end_position;
    unsigned char reserved_14[108];
};
struct Mesh {
    unsigned char reserved[0x94];
    struct Channel *channels;
    int count;
};
struct Index { int value; };
extern const float zero_position;

__declspec(noinline) int __fastcall reached_position(struct Mesh *mesh, struct Index index, float position)
{
    if (position < zero_position || position > mesh->channels[index.value].end_position)
        position = mesh->channels[index.value].end_position;
    if (mesh->count > index.value && index.value >= 0 && mesh->channels[index.value].position >= position)
        return 1;
    return 0;
}

int __fastcall reached_end(struct Mesh *mesh, struct Index index)
{
    return reached_position(mesh, index, -1.0f);
}
