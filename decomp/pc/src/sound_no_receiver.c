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
extern int NoReceiverReported;
extern const char NoReceiverMessage[];
extern void __stdcall SND_fn_vDisplayError(int code, const char *message);

#pragma code_seg(".text$V75c5f0")
__declspec(noinline) void __cdecl no_receiver_parameters(const Input *input, Mixer *mix)
{
    mix->volume = 0;
    mix->pitch = 65536;
    if (!NoReceiverReported) {
        NoReceiverReported = 1;
        SND_fn_vDisplayError(8, NoReceiverMessage);
    }
    switch (mix->mode) {
    case 1: mix->output.channels.dolby = 0; /* fall through */
    case 0: mix->output.channels.pan = 64; break;
    case 2: mix->output.angle = 0; break;
    }
}
