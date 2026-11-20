struct CalendarTime {
    int second, minute, hour, day, month, year, weekday, day_of_year, dst;
};
extern long _timezone, _dstbias;
extern int _daylight, _days[];
void __tzset(void);
int _isindst(struct CalendarTime *);
struct CalendarTime *gmtime(const long *);

struct CalendarTime *__cdecl steam_localtime(const long *seconds)
{
    struct CalendarTime *out;
    long offset;
    if (*seconds < 0)
        return 0;
    __tzset();
    if (*seconds > 3L * 86400 && *seconds < 2147483647L - 3L * 86400) {
        offset = *seconds - _timezone;
        out = gmtime(&offset);
        if (_daylight && _isindst(out)) {
            offset -= _dstbias;
            out = gmtime(&offset);
            out->dst = 1;
        }
    } else {
        out = gmtime(seconds);
        if (_daylight && _isindst(out)) {
            offset = out->second - _dstbias - _timezone;
            out->dst = 1;
        } else {
            offset = out->second - _timezone;
        }
        out->second = offset % 60;
        if (out->second < 0) {
            out->second += 60;
            offset -= 60;
        }
        offset = out->minute + offset / 60;
        out->minute = offset % 60;
        if (out->minute < 0) {
            out->minute += 60;
            offset -= 60;
        }
        offset = out->hour + offset / 60;
        out->hour = offset % 24;
        if (out->hour < 0) {
            out->hour += 24;
            offset -= 24;
        }
        offset /= 24;
        if (offset > 0) {
            out->weekday = (out->weekday + offset) % 7;
            out->day += offset;
            out->day_of_year += offset;
        } else if (offset < 0) {
            out->weekday = (out->weekday + offset + 7) % 7;
            out->day += offset;
            if (out->day <= 0) {
                out->day += 31;
                out->day_of_year = 364;
                out->month = 11;
                --out->year;
            } else {
                out->day_of_year += offset;
            }
        }
    }
    return out;
}

long __cdecl steam_loctotime_t(int year, int month, int day, int hour,
                                int minute, int second, int dst)
{
    struct CalendarTime local;
    int day_of_year;
    long result;
    year -= 1900;
    if (year < 70 || year > 138)
        return -1;
    day_of_year = _days[month - 1] + day;
    if (((year % 4 == 0 && year % 100 != 0) || (year + 1900) % 400 == 0) && month > 2)
        ++day_of_year;
    __tzset();
    result = (((year - 70) * 365 + (year - 1) / 4 - (year - 1) / 100 +
            (year + 299) / 400 - 17 + day_of_year) * 24 + hour) * 60 + minute;
    result = result * 60 + second + _timezone;
    local.year = year;
    local.month = month - 1;
    local.day_of_year = day_of_year;
    local.hour = hour;
    local.minute = minute;
    local.second = second;
    if (dst == 1 || (dst == -1 && _daylight && _isindst(&local)))
        result += _dstbias;
    return result;
}
