typedef int Fixed;
typedef struct {
    Fixed pitch, volume;
    unsigned char pan;
    unsigned char reserved[0x1b];
    unsigned flags;
} Input;
typedef struct {
    Fixed pitch, volume;
    int mode;
    union { struct { unsigned char pan, dolby; } channels; int angle; } output;
} Mixer;
typedef struct {
    unsigned unknown00, type;
    unsigned unknown08[2], field10;
} Resource;

extern int SoundReceiverCount;
extern unsigned __cdecl resource_channels(const Resource *resource);
extern void __cdecl spatial_parameters(const Input *input, Mixer *mix);

static __forceinline void direct_sound_parameters(const Input *input, Mixer *mix)
{
    mix->volume = input->volume;
    mix->pitch = input->pitch;
    switch (mix->mode) {
    case 0: mix->output.channels.pan = input->pan; break;
    case 1: mix->output.channels.pan = input->pan; mix->output.channels.dolby = 0; break;
    case 2: mix->output.angle = 0; break;
    }
}

extern void __cdecl no_receiver_parameters(const Input *input, Mixer *mix);

#pragma code_seg(".text$V75cad0")
__declspec(noinline) void __stdcall SND_fn_vSndParToPrivSndPar(const Input *input, Mixer *mix, int mode)
{
    mix->mode = mode;
    if (input->flags & 1)
        direct_sound_parameters(input, mix);
    else if (!SoundReceiverCount)
        no_receiver_parameters(input, mix);
    else
        spatial_parameters(input, mix);
}
#pragma code_seg(".text$V75cb40")
void __stdcall SND_fn_vBestConvertionForSndParToPrivSndPar(const Input *input, Mixer *mix, int mode, const Resource *resource)
{
    int chosen = mode;
    if (resource && resource->type == 1 && resource_channels(resource) > 1)
        chosen = 0;
    if (mode == 2 && (input->flags & 1))
        chosen = 0;
    SND_fn_vSndParToPrivSndPar(input, mix, chosen);
    if (resource && resource_channels(resource) > 1 && !resource->field10)
        mix->output.channels.pan = 64;
}
