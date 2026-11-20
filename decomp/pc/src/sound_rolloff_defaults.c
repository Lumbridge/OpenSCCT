typedef struct { int first, second; } DistancePair;
typedef struct { DistancePair limits, fade; } SoundRollOff;
extern SoundRollOff DefaultRollOff;

#pragma code_seg(".text$B")
void __stdcall SND_fn_vGetDefaultRollOff(SoundRollOff *out)
{
    out->limits = DefaultRollOff.limits;
    out->fade = DefaultRollOff.fade;
}
