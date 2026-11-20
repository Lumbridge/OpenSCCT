typedef unsigned __int64 FileTime;
__declspec(dllimport) void __stdcall GetSystemTimeAsFileTime(FileTime *);

long __cdecl steam_time(long *result)
{
    FileTime now;
    long seconds;
    GetSystemTimeAsFileTime(&now);
    seconds = (long)((now - 116444736000000000ui64) / 10000000ui64);
    if (result)
        *result = seconds;
    return seconds;
}

double __cdecl steam_difftime(long end, long start)
{
    return (double)(end - start);
}

struct CalendarTime {
    int second, minute, hour, day, month, year, weekday, day_of_year, dst;
};
struct ThreadData {
    char before_calendar[0x44];
    struct CalendarTime *calendar;
};
extern struct ThreadData *_getptd(void);
extern struct CalendarTime tb;
extern int _days[], _lpdays[];
void *malloc(unsigned);

struct CalendarTime *__cdecl steam_gmtime(const long *seconds)
{
    long remaining = *seconds;
    int leap = 0, year, month;
    int *months;
    struct ThreadData *thread = _getptd();
    struct CalendarTime *out;
    if (remaining < 0)
        return 0;
    if (!thread->calendar && !(thread->calendar = malloc(sizeof(*out))))
        out = &tb;
    else
        out = thread->calendar;
    year = remaining / (86400L * 1461);
    remaining -= year * (86400L * 1461);
    year = 70 + 4 * year;
    if (remaining >= 86400L * 365) {
        ++year;
        remaining -= 86400L * 365;
        if (remaining >= 86400L * 365) {
            ++year;
            remaining -= 86400L * 365;
            if (remaining >= 86400L * 366) {
                ++year;
                remaining -= 86400L * 366;
            } else {
                leap = 1;
            }
        }
    }
    out->year = year;
    out->day_of_year = remaining / 86400L;
    remaining -= out->day_of_year * 86400L;
    months = leap ? _lpdays : _days;
    for (month = 1; months[month] < out->day_of_year; ++month) { }
    out->month = --month;
    out->day = out->day_of_year - months[month];
    out->weekday = (*seconds / 86400L + 4) % 7;
    out->hour = remaining / 3600L;
    remaining -= out->hour * 3600L;
    out->minute = remaining / 60L;
    remaining -= out->minute * 60L;
    out->second = remaining;
    out->dst = 0;
    return out;
}
