typedef int SoundFixed;
typedef struct {
    SoundFixed x, y, z;
} SoundVector;

extern SoundFixed __stdcall SND_fn_rAbsRealSnd(SoundFixed value);
extern SoundFixed __stdcall SND_fn_rPseudoScalaireNormeVectorSnd(const SoundVector *a, const SoundVector *b);
SoundFixed __stdcall SND_fn_rPseudoNormeVectorSnd(const SoundVector *vector);

/* The channel uses 64 for the centre, with endpoints 0 and 128. */
static __forceinline unsigned char position_pan(const SoundVector *source, const SoundVector *listener,
                                                const SoundVector *axis, int reverse)
{
    SoundVector delta;
    SoundFixed extent, component;
    int divisor, pan;

    delta.x = source->x - listener->x;
    delta.y = source->y - listener->y;
    delta.z = source->z - listener->z;
    extent = SND_fn_rPseudoNormeVectorSnd(&delta);
    component = SND_fn_rPseudoScalaireNormeVectorSnd(&delta, axis);
    divisor = (2 * extent) / 256;

    if (!divisor) {
        pan = 64;
    } else if (component <= -extent) {
        pan = reverse ? 128 : 0;
    } else if (component >= extent) {
        pan = reverse ? 0 : 128;
    } else {
        int displacement = ((component / 256) * 128) / divisor;
        pan = reverse ? 64 - displacement : 64 + displacement;
        if (pan < 0)
            pan = 0;
        if (pan > 128)
            pan = 128;
    }
    return (unsigned char)pan;
}

#pragma code_seg(".text$B")
unsigned char __stdcall SND_fn_ucPositionToPan(const SoundVector *source, const SoundVector *listener,
                                              const SoundVector *unused, const SoundVector *right)
{
    return position_pan(source, listener, right, 0);
}

#pragma code_seg(".text$C")
unsigned char __stdcall SND_fn_ucPositionToDolby(const SoundVector *source, const SoundVector *listener,
                                                const SoundVector *direction)
{
    return position_pan(source, listener, direction, 1);
}

/* A two-axis approximation uses the larger magnitude and half the smaller. */
static __inline SoundFixed approximate_norm(SoundFixed a, SoundFixed b)
{
    a = SND_fn_rAbsRealSnd(a);
    b = SND_fn_rAbsRealSnd(b);
    if (a < b)
        return b + (a >> 1);
    return a + (b >> 1);
}

#pragma code_seg(".text$E")
__declspec(noinline) SoundFixed __stdcall SND_fn_rPseudoNormeVectorSnd(const SoundVector *vector)
{
    SoundFixed x = SND_fn_rAbsRealSnd(vector->x);
    SoundFixed y = SND_fn_rAbsRealSnd(vector->y);
    SoundFixed z = SND_fn_rAbsRealSnd(vector->z);
    return approximate_norm(approximate_norm(x, y), z);
}
