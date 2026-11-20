struct WideText {
    unsigned reserved;
    union { unsigned short storage[8]; void *heap; } buffer;
    unsigned length, capacity;
};
struct Record { struct WideText text; unsigned value; };
struct Argument { const void *address; };
/* The range stack argument packs a zero start and an all-elements count. */
extern void __fastcall copy_text(struct WideText *, struct Argument, unsigned __int64 range);

static __forceinline void assign_record(struct Record *output, const struct Record *input)
{
    struct Argument argument;
    argument.address = &input->text;
    copy_text(&output->text, argument, (unsigned __int64)~0u << 32);
    output->value = input->value;
}

struct Record *copy_records(const struct Record *first, const struct Record *last,
                            struct Record *output)
{
    for (; first != last; ++first, ++output)
        assign_record(output, first);
    return output;
}

struct Record *copy_backward(const struct Record *first, const struct Record *last,
                             struct Record *output_end)
{
    if (first != last) {
        do {
            assign_record(--output_end, --last);
        } while (last != first);
    }
    return output_end;
}

extern void __cdecl free_buffer(void *buffer);

void destroy_records(struct Record *first, struct Record *last)
{
    for (; first != last; ++first) {
        if (first->text.capacity >= 8)
            free_buffer(first->text.buffer.heap);
        first->text.capacity = 7;
        first->text.length = 0;
        first->text.buffer.storage[0] = 0;
    }
}
