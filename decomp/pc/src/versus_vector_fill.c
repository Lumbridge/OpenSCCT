struct Element4 { unsigned char bytes[4]; };
extern void __cdecl fill_4(struct Element4 *first, unsigned count,
                            const struct Element4 *value, void *allocator,
                            const struct Element4 *type_hint);

struct Element4 *__fastcall fill_end_4(void *allocator, unsigned unused,
                                          struct Element4 *first, unsigned count,
                                          const struct Element4 *volatile value)
{
    fill_4(first, count, value, allocator, value);
    return first + count;
}

struct Element8 { unsigned char bytes[8]; };
extern void __cdecl fill_8(struct Element8 *first, unsigned count,
                            const struct Element8 *value, void *allocator,
                            const struct Element8 *type_hint);

struct Element8 *__fastcall fill_end_8(void *allocator, unsigned unused,
                                          struct Element8 *first, unsigned count,
                                          const struct Element8 *volatile value)
{
    fill_8(first, count, value, allocator, value);
    return first + count;
}

struct Element32 { unsigned char bytes[32]; };
extern void __cdecl fill_32(struct Element32 *first, unsigned count,
                            const struct Element32 *value, void *allocator,
                            const struct Element32 *type_hint);

struct Element32 *__fastcall fill_end_32(void *allocator, unsigned unused,
                                          struct Element32 *first, unsigned count,
                                          const struct Element32 *volatile value)
{
    fill_32(first, count, value, allocator, value);
    return first + count;
}
