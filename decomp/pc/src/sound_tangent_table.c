extern const float SoundZero;
extern const double SoundZeroDouble;
typedef struct { float angle, tangent; } AngleEntry;
extern const AngleEntry SoundTangentTable[91];

#pragma code_seg(".text$V747690")
float __cdecl approximate_atan(float tangent)
{
    float magnitude = tangent < SoundZero ? -tangent : tangent;
    int low = 0;
    int high = 90;
    int index = 0;
    if (magnitude <= SoundTangentTable[0].tangent) {
        index = 0;
    } else if (magnitude >= SoundTangentTable[90].tangent) {
        index = 90;
    } else {
        int last;
        do {
            last = index;
            index = (high + low) / 2;
            if (magnitude > SoundTangentTable[index].tangent)
                low = index;
            else
                high = index;
        } while (last != index);
        if (!index && magnitude > SoundZeroDouble)
            index = 1;
    }
    if (tangent < SoundZero)
        return -SoundTangentTable[index].angle;
    return SoundTangentTable[index].angle;
}
