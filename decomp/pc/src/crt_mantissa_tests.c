int steam_is_zero_mantissa(const unsigned *words)
{
    int i;
    for (i = 0; i < 3; ++i)
        if (words[i]) return 0;
    return 1;
}
int steam_is_zero_tail(const unsigned *words, int bit)
{
    int i = bit / 32;
    unsigned mask = ~(~0u << (31 - bit % 32));
    if (words[i] & mask) return 0;
    while (++i < 3)
        if (words[i]) return 0;
    return 1;
}
