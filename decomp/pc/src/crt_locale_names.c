void __cdecl free(void *);
void *__cdecl malloc(unsigned);
unsigned __cdecl strlen(const char *);
char *__cdecl strcpy(char *, const char *);

struct TimeNames {
    char *short_day[7], *long_day[7];
    char *short_month[12], *long_month[12];
    char *am_pm[2];
    char *short_date, *long_date, *time;
};
extern struct TimeNames *__lc_time_curr;

struct NumericConventions {
    char *decimal_point, *thousands_sep, *grouping;
    char *int_currency_symbol, *currency_symbol;
    char *mon_decimal_point, *mon_thousands_sep, *mon_grouping;
    char *positive_sign, *negative_sign;
};
extern struct NumericConventions *__lconv;
extern struct NumericConventions __lconv_c;

#define FREE_PRIVATE(member) \
    if (data->member != __lconv->member && data->member != __lconv_c.member) \
        free(data->member)

void __cdecl steam_free_lconv_num(struct NumericConventions *data)
{
    if (data) {
        FREE_PRIVATE(decimal_point);
        FREE_PRIVATE(thousands_sep);
        FREE_PRIVATE(grouping);
    }
}

void __cdecl steam_free_lconv_mon(struct NumericConventions *data)
{
    if (data) {
        FREE_PRIVATE(int_currency_symbol);
        FREE_PRIVATE(currency_symbol);
        FREE_PRIVATE(mon_decimal_point);
        FREE_PRIVATE(mon_thousands_sep);
        FREE_PRIVATE(mon_grouping);
        FREE_PRIVATE(positive_sign);
        FREE_PRIVATE(negative_sign);
    }
}

/* Weekdays are released Monday through Sunday. Sunday has array index zero. */
#define FREE_WEEK(days) \
    free(days[1]); free(days[2]); free(days[3]); free(days[4]); \
    free(days[5]); free(days[6]); free(days[0])
#define FREE_YEAR(months) \
    free(months[0]); free(months[1]); free(months[2]); free(months[3]); \
    free(months[4]); free(months[5]); free(months[6]); free(months[7]); \
    free(months[8]); free(months[9]); free(months[10]); free(months[11])

void __cdecl steam_free_lc_time(struct TimeNames *data)
{
    if (data) {
        FREE_WEEK(data->short_day);
        FREE_WEEK(data->long_day);
        FREE_YEAR(data->short_month);
        FREE_YEAR(data->long_month);
        free(data->am_pm[0]);
        free(data->am_pm[1]);
        free(data->short_date);
        free(data->long_date);
        free(data->time);
    }
}

#define DEFINE_NAMES(function, short_names, long_names, count) \
char *__cdecl function(void) \
{ \
    struct TimeNames *names = __lc_time_curr; \
    unsigned i, size = 0; \
    char *result, *cursor; \
    for (i = 0; i < count; ++i) \
        size += strlen(names->short_names[i]) + strlen(names->long_names[i]) + 2; \
    result = malloc(size + 1); \
    if (result) { \
        cursor = result; \
        for (i = 0; i < count; ++i) { \
            *cursor++ = ':'; \
            cursor += strlen(strcpy(cursor, names->short_names[i])); \
            *cursor++ = ':'; \
            cursor += strlen(strcpy(cursor, names->long_names[i])); \
        } \
        *cursor = 0; \
    } \
    return result; \
}

DEFINE_NAMES(steam_Getdays, short_day, long_day, 7)
DEFINE_NAMES(steam_Getmonths, short_month, long_month, 12)
