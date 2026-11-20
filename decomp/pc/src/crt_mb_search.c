struct MbInfo {
    int refs, codepage, enabled;
    unsigned locale;
    unsigned short fullwidth[6];
    unsigned char types[257];
    char case_map[256];
};
struct ThreadInfo { char preceding[0x60]; struct MbInfo *mb; };
extern struct MbInfo *__ptmbcinfo;
struct ThreadInfo *_getptd(void);
struct MbInfo *__updatetmbcinfo(void);
char *strchr(const char *, int);
char *strrchr(const char *, int);
char *strpbrk(const char *, const char *);
int _stricmp(const char *, const char *);
int __crtLCMapStringA(unsigned, unsigned, const char *, int, char *, int, unsigned, int);

#define CURRENT_MB() \
    struct MbInfo *mb = _getptd()->mb; \
    if (mb != __ptmbcinfo) mb = __updatetmbcinfo()
#define LEAD(c) (mb->types[(unsigned char)(c) + 1] & 4)

unsigned char *steam_mbschr(const unsigned char *text, unsigned key)
{
    unsigned short c;
    CURRENT_MB();
    if (!mb->enabled) return (unsigned char *)strchr((const char *)text, key);
    while ((c = *text) != 0) {
        if (LEAD(c)) {
            if (!*++text) return 0;
            if (key == ((c << 8) | *text)) return (unsigned char *)text - 1;
        } else if (key == c) break;
        ++text;
    }
    return key == c ? (unsigned char *)text : 0;
}
unsigned char *steam_mbsrchr(const unsigned char *text, unsigned key)
{
    const unsigned char *found = 0;
    unsigned c;
    CURRENT_MB();
    if (!mb->enabled) return (unsigned char *)strrchr((const char *)text, key);
    do {
        c = *text;
        if (LEAD(c)) {
            if (*++text) {
                if (key == ((c << 8) | *text)) found = text - 1;
            } else if (!found) found = text;
        } else if (key == c) found = text;
    } while (*text++);
    return (unsigned char *)found;
}
#define UPPER_CHARACTER(ptr, value) \
    raw = *ptr++; \
    if (LEAD(raw)) { \
        if (!*ptr) value = 0; \
        else { \
            int n = __crtLCMapStringA(mb->locale, 0x200, (const char *)ptr - 1, 2, \
                                      (char *)mapped, 2, mb->codepage, 1); \
            if (n == 1) value = mapped[0]; \
            else if (n == 2) value = (unsigned short)((mapped[0] << 8) + mapped[1]); \
            else return 0x7fffffff; \
            ++ptr; \
        } \
    } else value = (mb->types[raw + 1] & 0x10) ? mb->case_map[raw] : raw

int steam_mbsicmp(const unsigned char *left, const unsigned char *right)
{
    unsigned short a, b, raw;
    unsigned char mapped[2];
    CURRENT_MB();
    if (!mb->enabled) return _stricmp((const char *)left, (const char *)right);
    do {
        UPPER_CHARACTER(left, a);
        UPPER_CHARACTER(right, b);
        if (b != a) return b < a ? 1 : -1;
    } while (a);
    return 0;
}
