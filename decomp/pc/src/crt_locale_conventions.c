struct Conventions {
    char *decimal_point, *thousands_sep, *grouping;
    char *int_currency_symbol, *currency_symbol, *mon_decimal_point;
    char *mon_thousands_sep, *mon_grouping, *positive_sign, *negative_sign;
    char int_frac_digits, frac_digits, p_cs_precedes, p_sep_by_space;
    char n_cs_precedes, n_sep_by_space, p_sign_posn, n_sign_posn;
};
struct LocaleIdentifier { unsigned short language, country, code_page; };
struct LocaleInfo { char before_conventions[0x2c]; int *conventions_refs; };
extern struct LocaleInfo *__ptlocinfo;
extern struct LocaleIdentifier __lc_id[];
extern unsigned __lc_handle[];
extern struct Conventions __lconv_c, *__lconv, *__lconv_intl;
extern int *__lconv_intl_refcount, *__lconv_num_refcount, *__lconv_mon_refcount;
extern char __decimal_point[];
extern int __decimal_point_length;
void *malloc(unsigned);
void *calloc(unsigned, unsigned);
void free(void *);
int __getlocaleinfo(int, unsigned, unsigned, void *);
void __free_lconv_num(struct Conventions *);
void __free_lconv_mon(struct Conventions *);
enum { Monetary = 3, Numeric = 4 };

/* Windows returns semicolon-separated widths, while localeconv exposes bytes. */
#define CONVERT_GROUPING(field) \
    { \
        char *p = (field); \
        while (*p) { \
            if (*p >= '0' && *p <= '9') *p++ -= '0'; \
            else if (*p == ';') { \
                char *q = p; \
                do { *q = q[1]; ++q; } while (*q); \
            } else ++p; \
        } \
    }
#define RELEASE_UNUSED() \
    if (__lconv_intl_refcount && !*__lconv_intl_refcount && \
        __lconv_intl_refcount != __ptlocinfo->conventions_refs) { \
        free(__lconv_intl_refcount); \
        free(__lconv_intl); \
    }
#define READ_OPTION(kind, code, field) __getlocaleinfo(kind, locale, code, &next->field)

int steam_init_numeric(void)
{
    struct Conventions *next;
    int *refs;
    if (!__lc_handle[Numeric] && !__lc_handle[Monetary]) {
        RELEASE_UNUSED();
        __lconv_num_refcount = 0;
        __lconv_intl_refcount = 0;
        next = &__lconv_c;
        __lconv_intl = 0;
    } else {
        next = calloc(1, sizeof(*next));
        if (!next) return 1;
        *next = *__lconv;
        refs = malloc(sizeof(*refs));
        if (!refs) { free(next); return 1; }
        *refs = 0;
        if (__lc_handle[Numeric]) {
            int error;
            unsigned locale;
            __lconv_num_refcount = malloc(sizeof(*__lconv_num_refcount));
            if (!__lconv_num_refcount) {
                free(next);
                free(refs);
                return 1;
            }
            *__lconv_num_refcount = 0;
            locale = __lc_id[Numeric].country;
            error = READ_OPTION(1, 0x0e, decimal_point);
            error |= READ_OPTION(1, 0x0f, thousands_sep);
            error |= READ_OPTION(1, 0x10, grouping);
            if (error) {
                __free_lconv_num(next);
                free(next);
                free(refs);
                return -1;
            }
            CONVERT_GROUPING(next->grouping);
        } else {
            __lconv_num_refcount = 0;
            next->decimal_point = __lconv_c.decimal_point;
            next->thousands_sep = __lconv_c.thousands_sep;
            next->grouping = __lconv_c.grouping;
        }
        RELEASE_UNUSED();
        __lconv_intl_refcount = refs;
        __lconv_intl = next;
    }
    __lconv = next;
    __decimal_point[0] = *next->decimal_point;
    __decimal_point_length = 1;
    return 0;
}

int steam_init_monetary(void)
{
    struct Conventions *next;
    int *refs;
    if (!__lc_handle[Monetary] && !__lc_handle[Numeric]) {
        __lconv_mon_refcount = 0;
        __lconv_intl_refcount = 0;
        __lconv = &__lconv_c;
        __lconv_intl = 0;
    } else {
        next = calloc(1, sizeof(*next));
        if (!next) return 1;
        refs = malloc(sizeof(*refs));
        if (!refs) { free(next); return 1; }
        *refs = 0;
        if (__lc_handle[Monetary]) {
            int error;
            unsigned locale;
            __lconv_mon_refcount = malloc(sizeof(*__lconv_mon_refcount));
            if (!__lconv_mon_refcount) {
                free(next);
                free(refs);
                return 1;
            }
            *__lconv_mon_refcount = 0;
            locale = __lc_id[Monetary].country;
            error = READ_OPTION(1, 0x15, int_currency_symbol);
            error |= READ_OPTION(1, 0x14, currency_symbol);
            error |= READ_OPTION(1, 0x16, mon_decimal_point);
            error |= READ_OPTION(1, 0x17, mon_thousands_sep);
            error |= READ_OPTION(1, 0x18, mon_grouping);
            error |= READ_OPTION(1, 0x50, positive_sign);
            error |= READ_OPTION(1, 0x51, negative_sign);
            error |= READ_OPTION(0, 0x1a, int_frac_digits);
            error |= READ_OPTION(0, 0x19, frac_digits);
            error |= READ_OPTION(0, 0x54, p_cs_precedes);
            error |= READ_OPTION(0, 0x55, p_sep_by_space);
            error |= READ_OPTION(0, 0x56, n_cs_precedes);
            error |= READ_OPTION(0, 0x57, n_sep_by_space);
            error |= READ_OPTION(0, 0x52, p_sign_posn);
            error |= READ_OPTION(0, 0x53, n_sign_posn);
            if (error) {
                __free_lconv_mon(next);
                free(next);
                free(refs);
                return 1;
            }
            CONVERT_GROUPING(next->mon_grouping);
        } else {
            *next = __lconv_c;
            __lconv_mon_refcount = 0;
        }
        next->decimal_point = __lconv->decimal_point;
        next->thousands_sep = __lconv->thousands_sep;
        next->grouping = __lconv->grouping;
        __lconv = next;
        __lconv_intl_refcount = refs;
        __lconv_intl = next;
    }
    return 0;
}
