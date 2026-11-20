typedef unsigned short WideChar;

struct SystemTime {
    unsigned short year, month, weekday, day;
    unsigned short hour, minute, second, milliseconds;
};

__declspec(dllimport) void __stdcall GetLocalTime(struct SystemTime *);

/* Both widths use the same local clock and MM/DD/YY or HH:MM:SS layout. */
#define STORE_PAIR(out, i, n, character) \
    (out)[i] = (character)('0' + (n) / 10); \
    (out)[(i) + 1] = (character)('0' + (n) % 10)

#define DEFINE_STEAM_DATE(function, character) \
character *__cdecl function(character *out) \
{ \
    struct SystemTime now; \
    int year, month, day; \
    GetLocalTime(&now); \
    year = now.year; \
    month = now.month; \
    day = now.day; \
    year %= 100; \
    out[2] = out[5] = '/'; \
    out[8] = 0; \
    STORE_PAIR(out, 0, month, character); \
    STORE_PAIR(out, 3, day, character); \
    STORE_PAIR(out, 6, year, character); \
    return out; \
}

#define DEFINE_STEAM_TIME(function, character) \
character *__cdecl function(character *out) \
{ \
    struct SystemTime now; \
    int hour, minute, second; \
    GetLocalTime(&now); \
    hour = now.hour; \
    minute = now.minute; \
    second = now.second; \
    out[2] = out[5] = ':'; \
    out[8] = 0; \
    STORE_PAIR(out, 0, hour, character); \
    STORE_PAIR(out, 3, minute, character); \
    STORE_PAIR(out, 6, second, character); \
    return out; \
}

DEFINE_STEAM_DATE(steam_strdate, char)
DEFINE_STEAM_DATE(steam_wstrdate, WideChar)
DEFINE_STEAM_TIME(steam_strtime, char)
DEFINE_STEAM_TIME(steam_wstrtime, WideChar)
