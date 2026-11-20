typedef int SoundFixed;
typedef struct {
    SoundFixed x;
    SoundFixed y;
    SoundFixed z;
} SoundVector;

/* Reduced precision fixed-point squared length. */
SoundFixed __stdcall SND_fn_rNormeVectorSnd(const SoundVector *v)
{
    SoundFixed z = v->z >> 8;
    SoundFixed y = v->y >> 8;
    SoundFixed x = v->x >> 8;
    return x * x + y * y + z * z;
}

/* Keep the closest phase across 360-degree boundaries in a two-turn cycle. */
int __stdcall SND_fn_lSoundAngleToDolbyAngle(int previous, int current)
{
    int reference = previous % 360;
    int distance = current - reference;
    if (distance < 0)
        distance = -distance;
    if (distance > 180) {
        distance = 360 - distance;
        if (current > reference)
            distance = -distance;
    } else if (current < reference) {
        distance = -distance;
    }
    distance += previous;
    if (distance >= 720)
        distance -= 720;
    else if (distance < 0)
        distance += 720;
    return distance;
}
