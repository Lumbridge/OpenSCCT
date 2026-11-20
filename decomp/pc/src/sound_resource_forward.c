struct SoundResource;
void __cdecl sound_release_binary_backend(struct SoundResource *resource);

void __cdecl sound_release_binary_resource(struct SoundResource *resource)
{
    sound_release_binary_backend(resource);
}
