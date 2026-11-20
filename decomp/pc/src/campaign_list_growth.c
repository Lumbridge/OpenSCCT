/* VC 7.1 C fastcall passes scalar integers in EDX. These declarations
   describe the original member calls using stack arguments after ECX.
   For a small constant length, multiplying by the least positive binary32
   subnormal gives the length's exact bits in a four-byte stack slot. The
   range argument holds start in its low word and count in its high word. */
struct TextArgument { const void *value; };
struct Text {
    unsigned reserved;
    char storage[16];
    unsigned length, capacity;
};
struct Error {
    const void *table;
    unsigned base_fields[2];
    struct Text message;
};

extern const char list_message[];
extern const char error_table[], length_error_table[], length_error_type[];
extern void __fastcall assign_text(struct Text *, struct TextArgument, float length_bits);
extern void __fastcall construct_error(struct Error *);
extern void __fastcall copy_range(struct Text *, struct TextArgument, unsigned __int64 range);
__declspec(noreturn) void __stdcall throw_error(struct Error *, const void *type);

static __forceinline void raise_length_error(const char *message, float length_bits)
{
    struct Text source;
    struct Error error;
    struct TextArgument argument;

    source.capacity = 15;
    source.length = 0;
    source.storage[0] = 0;
    argument.value = message;
    assign_text(&source, argument, length_bits);

    construct_error(&error);
    error.table = error_table;
    error.message.capacity = 15;
    error.message.length = 0;
    error.message.storage[0] = 0;
    argument.value = &source;
    copy_range(&error.message, argument, (unsigned __int64)~0u << 32);

    error.table = length_error_table;
    throw_error(&error, length_error_type);
}

#define STACK_LENGTH(message) ((sizeof(message) - 1) * 1.40129846e-45f)


struct List { unsigned reserved; void *head; unsigned size; };
struct Count { unsigned value; };

#define GROW_LIST(Stride) \
    void __fastcall grow_##Stride(struct List *list, struct Count count) \
    { \
        if (~0u / Stride - list->size < count.value) \
            raise_length_error(list_message, STACK_LENGTH("list<T> too long")); \
        list->size += count.value; \
    }
GROW_LIST(4)
GROW_LIST(8)
GROW_LIST(12)
GROW_LIST(16)
GROW_LIST(28)
GROW_LIST(52)
GROW_LIST(56)
GROW_LIST(2836)
