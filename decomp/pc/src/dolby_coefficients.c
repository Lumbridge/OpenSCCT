/* Decoded from the two named Steam 252084 campaign exports. The addresses
   identify the original left/right coefficient tables in that image. */
#define DOLBY_LEFT_COEFFICIENTS  ((const float *)0x111e7838)
#define DOLBY_RIGHT_COEFFICIENTS ((const float *)0x111e7dd8)

float __stdcall SND_fn_fGetDolbyLtCoef(int angle)
{
    if (angle < 360)
        return DOLBY_LEFT_COEFFICIENTS[angle];
    return -DOLBY_LEFT_COEFFICIENTS[angle - 360];
}

float __stdcall SND_fn_fGetDolbyRtCoef(int angle)
{
    if (angle < 360)
        return DOLBY_RIGHT_COEFFICIENTS[angle];
    return -DOLBY_RIGHT_COEFFICIENTS[angle - 360];
}
