/* The Steam Windows CRT operates on 16-bit wide characters. */
typedef unsigned short WideChar;

unsigned __cdecl steam_wcslen(const WideChar *text)
{
    const WideChar *end = text;
    while (*end++ != 0) {
    }
    return (unsigned)(end - text - 1);
}

const WideChar *__cdecl steam_wcschr(const WideChar *text, WideChar key)
{
    while (*text && *text != key)
        ++text;
    if (*text == key)
        return text;
    return 0;
}

const WideChar *__cdecl steam_wcsrchr(const WideChar *text, WideChar key)
{
    const WideChar *begin = text;
    while (*text++ != 0) {
    }
    do {
        --text;
    } while (text != begin && *text != key);
    return *text == key ? text : 0;
}

const WideChar *__cdecl steam_wcspbrk(const WideChar *text, const WideChar *set)
{
    while (*text) {
        const WideChar *candidate = set;
        while (*candidate) {
            if (*candidate == *text)
                return text;
            ++candidate;
        }
        ++text;
    }
    return 0;
}
