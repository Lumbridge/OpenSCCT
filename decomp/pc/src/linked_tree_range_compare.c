int __cdecl memcmp(const void *, const void *, unsigned);
#pragma intrinsic(memcmp)
static __forceinline const int *smaller(const int *left, const int *right)
{
    return *right < *left ? right : left;
}
int compare_ranges(const unsigned char *first, const unsigned char *end,
                   const unsigned char *other, const unsigned char *other_end)
{
    int length = end - first, other_length = other_end - other;
    int order = memcmp(first, other, *smaller(&length, &other_length));
    if (order) return order;
    if (length < other_length) return -1;
    if (length > other_length) return 1;
    return 0;
}
