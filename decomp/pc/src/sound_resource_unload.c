typedef struct SoundResource {
    unsigned long id;
    int kind;
    unsigned long reserved;
    unsigned long data;
} SoundResource;
void __cdecl sound_release_binary_resource(SoundResource *resource);

void __stdcall SND_fn_vUnLoadResBinarySnd(SoundResource *resource)
{
    switch (resource->kind) {
    case 1:
    case 6:
        sound_release_binary_resource(resource);
        break;
    }
    resource->data = 0;
    resource->kind = 0;
}
