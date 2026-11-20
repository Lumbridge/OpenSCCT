void *__cdecl move_bytes(void *dest, const void *source, unsigned size);
unsigned char *__stdcall copy_range(const unsigned char *first, const unsigned char *end,
                                   unsigned char *dest)
{
    if (end == first) return dest;
    return (unsigned char *)move_bytes(dest, first, end - first) + (end - first);
}
struct String { unsigned char *first, *end, *limit; };
struct Count { unsigned value; };
extern unsigned char *__stdcall allocate_bytes(unsigned);
extern void __cdecl length_error(const char *);
extern const char string_name[];
void __fastcall reserve_empty(struct String *string, struct Count count)
{
    unsigned n = count.value;
    if (n <= ~0u && n > 0) {
        string->first = allocate_bytes(n);
        string->end = string->first;
        string->limit = string->first + n;
    } else length_error(string_name);
}
