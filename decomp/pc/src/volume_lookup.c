/* Each mode owns 128 signed 16.16 gain values in the retail data section. */
extern const int VolumeCurves[12][128];

#pragma code_seg(".text$V74ad90")
int __cdecl linear_to_volume(int amplitude, unsigned mode)
{
    int index = amplitude >> 9;
    if (index >= 128)
        index = 127;
    switch (mode) {
    case 0: return VolumeCurves[0][index];
    case 1: return VolumeCurves[1][index];
    case 2: return VolumeCurves[2][index];
    case 3: return VolumeCurves[3][index];
    case 4: return VolumeCurves[4][index];
    case 5: return VolumeCurves[5][index];
    case 6: return VolumeCurves[6][index];
    case 7: return VolumeCurves[7][index];
    case 8: return VolumeCurves[8][index];
    case 9: return VolumeCurves[9][index];
    case 10: return VolumeCurves[10][index];
    case 11: return VolumeCurves[11][index];
    default: return -96 * 65536;
    }
}
