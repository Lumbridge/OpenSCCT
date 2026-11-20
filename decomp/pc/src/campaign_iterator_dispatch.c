/* The fourth argument occupies a stack slot for an empty iterator tag. */
struct IteratorTag { unsigned char : 8; };

extern void *copy_forward(const void *, const void *, void *, struct IteratorTag);
extern void *copy_backward(const void *, const void *, void *, struct IteratorTag);
extern void destroy_range(void *, void *, void *, struct IteratorTag);

void *dispatch_forward(const void *first, const void *last, void *output)
{
    struct IteratorTag tag;
    return copy_forward(first, last, output, tag);
}

void *dispatch_backward(const void *first, const void *last, void *output_end)
{
    struct IteratorTag tag;
    return copy_backward(first, last, output_end, tag);
}

void dispatch_destroy(void *first, void *last, void *allocator)
{
    struct IteratorTag tag;
    destroy_range(first, last, allocator, tag);
}
