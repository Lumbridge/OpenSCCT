typedef union {
    double number;
    unsigned words[2];
    unsigned short halves[4];
    struct { unsigned low; unsigned fraction : 20, exponent : 11, sign : 1; } fields;
} DoubleBits;
#define BITS(x) (*(DoubleBits *)&(x))
#define CLASS_BITS(x) ((unsigned short)(BITS(x).halves[3] & 0x7ff8))
#define SIGNAL_NAN(x) (CLASS_BITS(x) == 0x7ff0 && ((BITS(x).words[1] & 0x7ffff) || BITS(x).words[0]))
#define QUIET_NAN(x) (CLASS_BITS(x) == 0x7ff8)
#define EXP_BITS(x) ((unsigned short)(BITS(x).halves[3] & 0x7ff0))

int steam_isnan(double value) { if (SIGNAL_NAN(value) || QUIET_NAN(value)) return 1; return 0; }
int steam_sptype(double value)
{
    if (BITS(value).words[1] == 0x7ff00000 && !BITS(value).words[0]) return 1;
    if (BITS(value).words[1] == 0xfff00000 && !BITS(value).words[0]) return 2;
    if (QUIET_NAN(value)) return 3;
    if (SIGNAL_NAN(value)) return 4;
    return 0;
}
double steam_copysign(double magnitude, double sign)
{
    double result;
    BITS(result).fields.low = BITS(magnitude).fields.low;
    BITS(result).words[1] = BITS(magnitude).words[1];
    BITS(result).fields.sign = BITS(sign).fields.sign;
    return result;
}
double steam_chgsign(double value)
{
    double result;
    BITS(result).words[0] = BITS(value).words[0];
    BITS(result).words[1] = BITS(value).words[1];
    BITS(result).words[1] = (BITS(result).words[1] & 0x7fffffff) | (~BITS(value).words[1] & 0x80000000);
    return result;
}
int steam_get_exp(double value)
{
    return (short)(((BITS(value).halves[3] >> 4) & 0x7ff) - 1022);
}
double steam_set_exp(double value, int exponent)
{
    double result = value;
    BITS(result).halves[3] = (unsigned short)((BITS(value).halves[3] & 0x800f) | ((exponent + 1022) << 4));
    return result;
}
double steam_set_bexp(double value, int exponent)
{
    double result = value;
    BITS(result).halves[3] = (unsigned short)((BITS(value).halves[3] & 0x800f) | (exponent << 4));
    return result;
}

int _sptype(double);
int _fpclass(double);
double _frnd(double);

int steam_fpclass(double value)
{
    int sign;
    if (EXP_BITS(value) == 0x7ff0) {
        switch (_sptype(value)) {
        case 1: return 0x200;
        case 2: return 0x004;
        case 3: return 0x002;
        default: return 0x001;
        }
    }
    sign = BITS(value).halves[3] & 0x8000;
    if (EXP_BITS(value) == 0 && ((BITS(value).words[1] & 0xfffff) || BITS(value).words[0]))
        return sign ? 0x10 : 0x80;
    if (value == 0.0) return sign ? 0x20 : 0x40;
    return sign ? 0x08 : 0x100;
}

#pragma optimize("p", on)
int steam_d_inttype(double value)
{
    double rounded;
    if (!(_fpclass(value) & 0x90)) {
        rounded = _frnd(value);
        if (rounded == value) {
            rounded = value / 2.0;
            return _frnd(rounded) == rounded ? 2 : 1;
        }
    }
    return 0;
}
