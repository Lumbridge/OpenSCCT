typedef unsigned short WideChar;
enum CharacterType {
    Upper = 1, Lower = 2, Digit = 4, Space = 8, Punctuation = 16,
    Control = 32, Blank = 64, HexDigit = 128, Alphabetic = 0x103
};
struct LocaleInfo {
    unsigned char before_multibyte_width[0x28];
    int multibyte_width;
    unsigned char before_types[0x1c];
    const unsigned short *types;
};
struct ThreadInfo {
    unsigned char before_locale[0x64];
    struct LocaleInfo *locale;
};
struct ThreadInfo *_getptd(void);
extern struct LocaleInfo *__ptlocinfo;
struct LocaleInfo *__updatetlocinfo(void);
int __isctype_mt(struct LocaleInfo *, int, int);
int iswctype(WideChar, WideChar);
int _wchartodigit(int);

#define GET_LOCALE() \
    struct LocaleInfo *locale = _getptd()->locale; \
    if (locale != __ptlocinfo) locale = __updatetlocinfo()
#define TEST_TYPE(value, mask) \
    (locale->multibyte_width > 1 ? __isctype_mt(locale, (value), (mask)) : \
                                  (locale->types[value] & (mask)))

#define CHARACTER_TEST(name, mask) \
    int steam_is##name(int value) { GET_LOCALE(); return TEST_TYPE(value, mask); } \
    int steam_isw##name(WideChar value) { return iswctype(value, mask); }

CHARACTER_TEST(alpha, Alphabetic)
CHARACTER_TEST(upper, Upper)
CHARACTER_TEST(lower, Lower)
CHARACTER_TEST(digit, Digit)
CHARACTER_TEST(xdigit, HexDigit)
CHARACTER_TEST(space, Space)
CHARACTER_TEST(punct, Punctuation)
CHARACTER_TEST(alnum, Alphabetic | Digit)
CHARACTER_TEST(print, Alphabetic | Digit | Punctuation | Blank)
CHARACTER_TEST(graph, Alphabetic | Digit | Punctuation)
CHARACTER_TEST(cntrl, Control)

#define DECIMAL_DIGIT(c) ((c) >= '0' && (c) <= '9' ? (c) - '0' : -1)
#define READ_INTEGER(type, digit) \
    int c = *text++, sign = c; \
    type number; \
    if (c == '-' || c == '+') c = *text++; \
    number = 0; \
    while ((c = digit(c)) != -1) { \
        number = number * 10 + c; \
        c = *text++; \
    } \
    if (sign == '-') return -number; \
    return number

#define NARROW_DECIMAL(type, function) \
type function(const unsigned char *text) \
{ \
    GET_LOCALE(); \
    while (TEST_TYPE(*text, Space)) ++text; \
    { READ_INTEGER(type, DECIMAL_DIGIT); } \
}
#define WIDE_DECIMAL(type, function) \
type function(const WideChar *text) \
{ \
    while (iswctype(*text, Space)) ++text; \
    { READ_INTEGER(type, _wchartodigit); } \
}
NARROW_DECIMAL(long, steam_atol)
NARROW_DECIMAL(__int64, steam_atoi64)
WIDE_DECIMAL(long, steam_wtol)
WIDE_DECIMAL(__int64, steam_wtoi64)
