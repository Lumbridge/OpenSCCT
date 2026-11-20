typedef int SoundFixed;

typedef struct {
    SoundFixed x;
    SoundFixed y;
    SoundFixed z;
} SoundVector;

extern SoundFixed __stdcall SND_fn_rMulRealRealQuickSnd(SoundFixed a, SoundFixed b);
extern SoundFixed __stdcall SND_fn_rMulRealRealSnd(SoundFixed a, SoundFixed b);

#pragma code_seg(".text$B")
SoundFixed __stdcall SND_fn_rScalaireVectorSnd(const SoundVector *a, const SoundVector *b)
{
    return SND_fn_rMulRealRealSnd(a->x, b->x)
         + SND_fn_rMulRealRealSnd(a->y, b->y)
         + SND_fn_rMulRealRealSnd(a->z, b->z);
}

#pragma code_seg(".text$C")
SoundFixed __stdcall SND_fn_rPseudoScalaireNormeVectorSnd(const SoundVector *a, const SoundVector *b)
{
    return SND_fn_rMulRealRealQuickSnd(a->x, b->x)
         + SND_fn_rMulRealRealQuickSnd(a->y, b->y)
         + SND_fn_rMulRealRealQuickSnd(a->z, b->z);
}

#pragma code_seg(".text$D")
void __stdcall SND_fn_vProduitVectorSnd(const SoundVector *a, const SoundVector *b, SoundVector *out)
{
    out->x = SND_fn_rMulRealRealSnd(a->y, b->z) - SND_fn_rMulRealRealSnd(a->z, b->y);
    out->y = SND_fn_rMulRealRealSnd(a->z, b->x) - SND_fn_rMulRealRealSnd(a->x, b->z);
    out->z = SND_fn_rMulRealRealSnd(a->x, b->y) - SND_fn_rMulRealRealSnd(a->y, b->x);
}
