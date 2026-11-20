typedef unsigned short WideChar;
struct LocaleInfo {
    unsigned refcount, codepage, collation;
    unsigned categories[6];
    int clike, width;
    unsigned char before_types[0x1c];
    const unsigned short *types;
};
struct ThreadInfo { unsigned char before_locale[0x64]; struct LocaleInfo *locale; };
extern struct ThreadInfo *_getptd(void);
extern struct LocaleInfo *__ptlocinfo;
struct LocaleInfo *__updatetlocinfo(void);
extern const unsigned short *_pwctype, *_pctype;
WideChar __towlower_mt(struct LocaleInfo *, WideChar);
WideChar __towupper_mt(struct LocaleInfo *, WideChar);
int __iswctype_mt(struct LocaleInfo *, WideChar, WideChar);
int __crtGetStringTypeW(unsigned, const WideChar *, int, WideChar *, unsigned, unsigned);
int __crtGetStringTypeA(unsigned, const char *, int, WideChar *, unsigned, unsigned, int);
int *_errno(void);
__declspec(dllimport) int __stdcall MultiByteToWideChar(unsigned, unsigned, const char *, int, WideChar *, int);
__declspec(dllimport) int __stdcall WideCharToMultiByte(unsigned, unsigned, const WideChar *, int,
                                                       char *, int, const char *, int *);

#define GET_LOCALE() \
    struct LocaleInfo *locale = _getptd()->locale; \
    if (locale != __ptlocinfo) locale = __updatetlocinfo()

int steam_isleadbyte(int value) { return _pctype[(unsigned char)value] & 0x8000; }
int steam_iswascii(WideChar value) { return value < 128; }

int steam_iswctype_mt(struct LocaleInfo *locale, WideChar c, WideChar mask)
{
    WideChar type;
    if (c == 0xffff) type = 0;
    else if (c < 256) type = _pwctype[c];
    else if (!__crtGetStringTypeW(1, &c, 1, &type, locale->codepage, locale->categories[2]))
        type = 0;
    return type & mask;
}
int steam_iswctype(WideChar c, WideChar mask)
{
    if (c < 256) return _pwctype[c] & mask;
    if (c == 0xffff) return 0;
    { GET_LOCALE(); return __iswctype_mt(locale, c, mask); }
}
WideChar steam_towlower(WideChar c)
{
    GET_LOCALE();
    if (c == 0xffff) return 0xffff;
    if (!locale->categories[2])
        return (WideChar)(c >= 'A' && c <= 'Z' ? c + ('a' - 'A') : c);
    return __towlower_mt(locale, c);
}
WideChar steam_towupper(WideChar c)
{
    GET_LOCALE();
    if (!locale->categories[2])
        return (WideChar)(c >= 'a' && c <= 'z' ? c - ('a' - 'A') : c);
    return __towupper_mt(locale, c);
}
int steam_isctype_mt(struct LocaleInfo *locale, int c, int mask)
{
    char text[3];
    int count;
    WideChar type;
    if ((unsigned)c + 1 <= 256)
        return locale->types[c] & mask;
    if (locale->types[(unsigned char)(c >> 8)] & 0x8000) {
        text[0] = (char)(c >> 8);
        text[1] = (char)c;
        text[2] = 0;
        count = 2;
    } else {
        text[0] = (char)c;
        text[1] = 0;
        count = 1;
    }
    if (!__crtGetStringTypeA(1, text, count, &type, locale->codepage, locale->categories[2], 1))
        return 0;
    return type & mask;
}
int steam_mbtowc_mt(struct LocaleInfo *locale, WideChar *out, const char *text, unsigned n)
{
    if (!text || !n) return 0;
    if (!*text) { if (out) *out = 0; return 0; }
    if (!locale->categories[2]) {
        if (out) *out = (unsigned char)*text;
        return 1;
    }
    if (locale->types[(unsigned char)*text] & 0x8000) {
        if (locale->width <= 1 || (int)n < locale->width ||
            !MultiByteToWideChar(locale->codepage, 9, text, locale->width, out, out != 0)) {
            if (n < (unsigned)locale->width || !text[1]) {
                *_errno() = 42;
                return -1;
            }
        }
        return locale->width;
    }
    if (!MultiByteToWideChar(locale->codepage, 9, text, 1, out, out != 0)) {
        *_errno() = 42;
        return -1;
    }
    return 1;
}
int steam_wctomb_mt(struct LocaleInfo *locale, char *out, WideChar c)
{
    if (!out) return 0;
    if (!locale->categories[2]) {
        if (c <= 255) { *out = (char)c; return 1; }
    } else {
        int substituted = 0, length = WideCharToMultiByte(locale->codepage, 0, &c, 1, out,
                                                         locale->width, 0, &substituted);
        if (length && !substituted) return length;
    }
    *_errno() = 42;
    return -1;
}
int __isctype_mt(struct LocaleInfo *, int, int);
int __crtLCMapStringA(unsigned, unsigned, const char *, int, char *, int, unsigned, int);

#define CASE_NARROW(function, low, high, delta, type, code) \
int function(struct LocaleInfo *locale, int value) \
{ \
    unsigned char input[3], output[3]; \
    int count; \
    if (!locale->categories[2] || (locale->clike && (unsigned)value <= 127)) \
        return value >= low && value <= high ? value + delta : value; \
    if ((unsigned)value < 256 && !(locale->width > 1 ? __isctype_mt(locale, value, type) : \
                                                       (locale->types[value] & type))) \
        return value; \
    if (locale->types[(unsigned char)(value >> 8)] & 0x8000) { \
        input[0] = (unsigned char)(value >> 8); \
        input[1] = (unsigned char)value; \
        input[2] = 0; count = 2; \
    } else { \
        input[0] = (unsigned char)value; input[1] = 0; count = 1; \
    } \
    count = __crtLCMapStringA(locale->categories[2], code, (const char *)input, count, \
                              (char *)output, 3, locale->codepage, 1); \
    if (!count) return value; \
    if (count == 1) return output[0]; \
    return (output[0] << 8) | output[1]; \
}
CASE_NARROW(steam_tolower_mt, 'A', 'Z', 32, 1, 0x100)
CASE_NARROW(steam_toupper_mt, 'a', 'z', -32, 2, 0x200)
