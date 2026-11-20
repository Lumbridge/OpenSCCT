typedef unsigned Word;
typedef union {
    char bytes[12];
    Word limb[3];
    unsigned short half[6];
} Extended96;

int __addl(Word, Word, Word *);
void __shl_12(Extended96 *);
void __shr_12(Extended96 *);
void __add_12(Extended96 *, const Extended96 *);

int steam_addl(Word a, Word b, Word *result)
{
    Word sum = a + b;
    int carry = 0;
    if (sum < a || sum < b) carry = 1;
    *result = sum;
    return carry;
}
void steam_add_12(Extended96 *out, const Extended96 *in)
{
    if (__addl(out->limb[0], in->limb[0], &out->limb[0]))
        if (__addl(out->limb[1], 1, &out->limb[1])) ++out->limb[2];
    if (__addl(out->limb[1], in->limb[1], &out->limb[1])) ++out->limb[2];
    __addl(out->limb[2], in->limb[2], &out->limb[2]);
}
void steam_shl_12(Extended96 *value)
{
    Word low_carry = value->limb[0] >> 31, mid_carry = value->limb[1] >> 31;
    value->limb[0] <<= 1;
    value->limb[1] = (value->limb[1] << 1) | low_carry;
    value->limb[2] = (value->limb[2] << 1) | mid_carry;
}
void steam_shr_12(Extended96 *value)
{
    Word high_carry = value->limb[2] << 31, mid_carry = value->limb[1] << 31;
    value->limb[2] >>= 1;
    value->limb[1] = (value->limb[1] >> 1) | high_carry;
    value->limb[0] = (value->limb[0] >> 1) | mid_carry;
}

void steam_mtold12(const char *digits, unsigned n, Extended96 *out)
{
    unsigned short exponent = 0x404e;
    Extended96 tmp;
    unsigned i;
    out->limb[0] = 0; out->limb[1] = 0; out->limb[2] = 0;
    for (i = 0; i < n; ++i) {
        tmp = *out;
        __shl_12(out);
        __shl_12(out);
        __add_12(out, &tmp);
        __shl_12(out);
        tmp.limb[0] = *digits;
        tmp.limb[1] = 0; tmp.limb[2] = 0;
        __add_12(out, &tmp);
        ++digits;
    }
    while (!out->limb[2]) {
        out->limb[2] = out->limb[1] >> 16;
        out->limb[1] = (out->limb[1] << 16) | (out->limb[0] >> 16);
        out->limb[0] <<= 16;
        exponent -= 16;
    }
    while (!(out->limb[2] & 0x8000)) {
        __shl_12(out);
        --exponent;
    }
    out->half[5] = exponent;
}

struct Extended80 { unsigned lo, hi; unsigned short exponent; };
extern Extended96 _pow10pos[], _pow10neg[];
void __ld12mul(Extended96 *, const Extended96 *);
int _RoundMan(Word *, int);
#define DWORD_AT(x, n) (*(Word *)((x)->bytes + (n)))

void steam_multtenpow12(Extended96 *out, int power, int precision)
{
    const Extended96 *table = _pow10pos - 8;
    Extended96 corrected;
    if (!power) return;
    if (power < 0) { power = -power; table = _pow10neg - 8; }
    if (!precision) out->half[0] = 0;
    while (power) {
        int digit = power & 7;
        power >>= 3;
        table += 7;
        if (digit) {
            const Extended96 *factor = table + digit;
            if (factor->half[0] >= 0x8000) {
                corrected = *factor;
                --DWORD_AT(&corrected, 2);
                factor = &corrected;
            }
            __ld12mul(out, factor);
        }
    }
}
int steam_ld12told(const Extended96 *in, struct Extended80 *out)
{
    int result = 0;
    Word mantissa[3];
    unsigned short exp = in->half[5] & 0x7fff, sign = in->half[5] & 0x8000;
    mantissa[0] = DWORD_AT(in, 6);
    mantissa[1] = DWORD_AT(in, 2);
    mantissa[2] = (Word)in->half[0] << 16;
    if (_RoundMan(mantissa, 64)) { mantissa[0] = 0x80000000; ++exp; }
    if (exp == 0x7fff) result = 1;
    out->hi = mantissa[0];
    out->lo = mantissa[1];
    out->exponent = sign | exp;
    return result;
}
