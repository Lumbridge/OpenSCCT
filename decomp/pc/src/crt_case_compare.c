typedef unsigned short WideChar;
struct LocaleInfo {
    int reference_count;
    unsigned codepage;
    unsigned collation_codepage;
    unsigned category[6];
};
struct ThreadData {
    char before_locale[0x64];
    struct LocaleInfo *locale;
};
extern struct LocaleInfo *__ptlocinfo;
struct ThreadData *_getptd(void);
struct LocaleInfo *__updatetlocinfo(void);
int __ascii_stricmp(const char *, const char *);
int __ascii_strnicmp(const char *, const char *, unsigned);
int __tolower_mt(struct LocaleInfo *, int);
int __iswctype_mt(struct LocaleInfo *, WideChar, WideChar);
int __crtLCMapStringW(unsigned, unsigned, const WideChar *, int, WideChar *, int, unsigned);
WideChar __towlower_mt(struct LocaleInfo *, WideChar);

#define GET_LOCALE() \
    struct LocaleInfo *locale = _getptd()->locale; \
    if (locale != __ptlocinfo) locale = __updatetlocinfo()

int steam_stricmp(const char *left, const char *right) {
    int a, b;
    GET_LOCALE();
    if (!locale->category[2])
        return __ascii_stricmp(left, right);
    do {
        a = __tolower_mt(locale, (unsigned char)*left++);
        b = __tolower_mt(locale, (unsigned char)*right++);
    } while (a && a == b);
    return a - b;
}
int steam_strnicmp(const char *left, const char *right, unsigned n) {
    int a, b;
    GET_LOCALE();
    if (n) {
        if (!locale->category[2])
            return __ascii_strnicmp(left, right, n);
        do {
            a = __tolower_mt(locale, (unsigned char)*left++);
            b = __tolower_mt(locale, (unsigned char)*right++);
        } while (--n && a && a == b);
        return a - b;
    }
    return 0;
}

#define WIDE_CASE(function, type, transformation) \
WideChar function(struct LocaleInfo *locale, WideChar value) { \
    WideChar result; \
    if (value == 0xffff) return 0xffff; \
    if (value < 256 && !__iswctype_mt(locale, value, type)) return value; \
    if (!__crtLCMapStringW(locale->category[2], transformation, &value, 1, &result, 1, locale->codepage)) \
        return value; \
    return result; \
}
WIDE_CASE(steam_towlower_mt, 1, 0x100)
WIDE_CASE(steam_towupper_mt, 2, 0x200)
