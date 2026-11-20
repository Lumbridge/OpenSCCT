/* Inputs are single-byte strings. Only uppercase ASCII letters change case. */
static __forceinline char sound_lower(char c)
{
    if (c >= 'A' && c <= 'Z')
        c += 'a' - 'A';
    return c;
}

long __stdcall SND_fn_lStriCmp(const char *left, const char *right)
{
    char a, b;
    do {
        a = *left;
        b = *right;
        a = sound_lower(a);
        b = sound_lower(b);
        if (a != b || b == 0)
            return a - b;
        ++left;
        ++right;
    } while (1);
}

char *__stdcall SND_fn_lStrLwr(char *text)
{
    char *p;
    for (p = text; *p != 0; ++p) {
        if (*p >= 'A' && *p <= 'Z')
            *p += 'a' - 'A';
    }
    return text;
}
