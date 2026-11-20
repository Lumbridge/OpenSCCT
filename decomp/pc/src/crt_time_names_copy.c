typedef unsigned size_t;
void *malloc(size_t);
size_t strlen(const char *);
char *strcpy(char *, const char *);
void *memcpy(void *, const void *, size_t);

struct TimeNames {
    char *short_day[7], *long_day[7];
    char *short_month[12], *long_month[12];
    char *am_pm[2];
    char *short_date, *long_date, *time;
    unsigned locale, calendar, references;
};
extern struct TimeNames *__lc_time_curr;

#define COPY_NAME(field) \
    result->field = cursor; \
    cursor += strlen(strcpy(cursor, names->field)) + 1

struct TimeNames *__cdecl steam_Gettnames(void)
{
    struct TimeNames *names = __lc_time_curr;
    struct TimeNames *result;
    char *cursor;
    size_t i, length = 0;
    for (i = 0; i < 7; ++i)
        length += strlen(names->short_day[i]) + strlen(names->long_day[i]) + 2;
    for (i = 0; i < 12; ++i)
        length += strlen(names->short_month[i]) + strlen(names->long_month[i]) + 2;
    length += strlen(names->am_pm[1]) + strlen(names->am_pm[0]) + 2;
    length += strlen(names->short_date) + 1;
    length += strlen(names->long_date) + 1;
    length += strlen(names->time) + 1;
    result = malloc(sizeof(*result) + length);
    if (result) {
        cursor = (char *)(result + 1);
        memcpy(result, __lc_time_curr, sizeof(*result));
        for (i = 0; i < 7; ++i) {
            COPY_NAME(short_day[i]);
            COPY_NAME(long_day[i]);
        }
        for (i = 0; i < 12; ++i) {
            COPY_NAME(short_month[i]);
            COPY_NAME(long_month[i]);
        }
        COPY_NAME(am_pm[0]);
        COPY_NAME(am_pm[1]);
        COPY_NAME(short_date);
        COPY_NAME(long_date);
        result->time = cursor;
        strcpy(cursor, names->time);
    }
    return result;
}
