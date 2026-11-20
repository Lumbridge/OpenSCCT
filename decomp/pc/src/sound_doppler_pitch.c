typedef int SoundFixed;
typedef struct { SoundFixed x, y, z; } SoundVector;

extern int DopplerPropagationSpeed;
extern SoundFixed __stdcall SND_fn_rPseudoNormeVectorSnd(const SoundVector *vector);
extern SoundFixed __stdcall SND_fn_rScalaireVectorSnd(const SoundVector *a, const SoundVector *b);
extern SoundFixed __stdcall SND_fn_rDivRealRealSnd(SoundFixed numerator, SoundFixed denominator);
#pragma code_seg(".text$B")
SoundFixed __stdcall SND_fn_rDopplerPitch(const SoundVector *source, const SoundVector *source_velocity,
                                          const SoundVector *listener, const SoundVector *listener_velocity)
{
    SoundVector delta;
    SoundFixed distance, speed, source_component, listener_component;
    if (!DopplerPropagationSpeed)
        return 65536;
    delta.x = (source->x - listener->x) >> 8;
    delta.y = (source->y - listener->y) >> 8;
    delta.z = (source->z - listener->z) >> 8;
    distance = SND_fn_rPseudoNormeVectorSnd(&delta);
    if (!distance)
        return 65536;
    if (distance > 64 * 65536)
        return 65536;
    speed = DopplerPropagationSpeed * distance;
    source_component = SND_fn_rScalaireVectorSnd(source_velocity, &delta);
    listener_component = SND_fn_rScalaireVectorSnd(listener_velocity, &delta);
    if (source_component >= speed || listener_component >= speed)
        return 65536;
    return SND_fn_rDivRealRealSnd(speed - source_component, speed - listener_component);
}
