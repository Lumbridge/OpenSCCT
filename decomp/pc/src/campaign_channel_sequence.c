struct Index { int value; };
struct Name { int value; };
struct Clip;
struct ClipRef { struct Clip *ptr; };
struct Mesh;
struct Bank;
struct BankMethods {
    void *reserved[24];
    struct Clip *(__fastcall *lookup)(struct Bank *, struct Name);
};
struct Bank { const struct BankMethods *methods; };
struct Channel {
    struct Name sequence;
    unsigned char reserved_04[0x40];
    unsigned char bank;
    unsigned char reserved_45[0x3b];
};
struct Methods {
    void *reserved_00[45];
    struct Clip *(__fastcall *lookup)(struct Mesh *, struct Name);
    void *reserved_b8[4];
    float (__fastcall *frames)(struct Mesh *, struct ClipRef);
    float (__fastcall *rate)(struct Mesh *, struct ClipRef);
};
struct Mesh {
    const struct Methods *methods;
    unsigned char reserved_04[0x74];
    struct Bank **banks;
    unsigned char reserved_7c[0x18];
    struct Channel *channels;
};
extern const float zero_frames, missing_rate;

struct Clip *__fastcall channel_clip(struct Mesh *mesh, struct Index index)
{
    const struct Channel *channel = &mesh->channels[index.value];
    struct Bank *bank = mesh->banks[channel->bank];
    return bank->methods->lookup(bank, channel->sequence);
}

float __fastcall channel_frames(struct Mesh *mesh, struct Index index)
{
    struct ClipRef clip;
    clip.ptr = mesh->methods->lookup(mesh, mesh->channels[index.value].sequence);
    return clip.ptr ? mesh->methods->frames(mesh, clip) : zero_frames;
}

float __fastcall channel_rate(struct Mesh *mesh, struct Index index)
{
    struct ClipRef clip;
    clip.ptr = mesh->methods->lookup(mesh, mesh->channels[index.value].sequence);
    return clip.ptr ? mesh->methods->rate(mesh, clip) : missing_rate;
}
