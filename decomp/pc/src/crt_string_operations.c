typedef unsigned short WideChar;

WideChar *__cdecl steam_wcscpy(WideChar *dest, const WideChar *source)
{
    WideChar *next = dest;
    while ((*next++ = *source++) != 0) {
    }
    return dest;
}

WideChar *__cdecl steam_wcscat(WideChar *dest, const WideChar *source)
{
    WideChar *next = dest;
    while (*next)
        ++next;
    while ((*next++ = *source++) != 0) {
    }
    return dest;
}

WideChar *__cdecl steam_wcsncpy(WideChar *dest, const WideChar *source, unsigned count)
{
    WideChar *start = dest;
    if (count) {
        while ((*dest++ = *source++) != 0 && --count) {
        }
        if (count)
            while (--count)
                *dest++ = 0;
    }
    return start;
}

WideChar *__cdecl steam_wcsncat(WideChar *dest, const WideChar *source, unsigned count)
{
    WideChar *start = dest;
    while (*dest++ != 0) {
    }
    --dest;
    while (count--) {
        if ((*dest++ = *source++) == 0)
            return start;
    }
    *dest = 0;
    return start;
}

int __cdecl steam_wcsncmp(const WideChar *left, const WideChar *right, unsigned limit)
{
    if (!limit)
        return 0;
    while (--limit && *left && *left == *right) {
        ++left;
        ++right;
    }
    return (int)*left - (int)*right;
}

int __cdecl steam_wcscmp(const WideChar *left, const WideChar *right)
{
    int delta;
    while ((delta = (int)*left - (int)*right) == 0 && *right) {
        ++left;
        ++right;
    }
    if (delta < 0)
        return -1;
    if (delta > 0)
        return 1;
    return delta;
}

const WideChar *__cdecl steam_wcsstr(const WideChar *text, const WideChar *pattern)
{
    const WideChar *start = text;
    if (!*pattern)
        return text;
    while (*start) {
        const WideChar *cursor = start;
        const WideChar *wanted = pattern;
        while (*cursor && *wanted && (int)*cursor - (int)*wanted == 0) {
            ++cursor;
            ++wanted;
        }
        if (!*wanted)
            return start;
        ++start;
    }
    return 0;
}
