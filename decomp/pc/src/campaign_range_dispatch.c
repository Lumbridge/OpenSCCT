/* The retail dispatch adds an empty category object as the last stack
   argument. The unnamed bit-field reserves its byte without introducing
   a value that the range or fill operation would have to initialize. */
struct Category { unsigned char : 8; };

extern void *copy_range(const void *first, const void *last, void *output,
                        void *allocator, struct Category category);
extern void fill_count(void *first, unsigned count, const void *value,
                       void *allocator, struct Category category);

void *dispatch_copy(const void *first, const void *last, void *output, void *allocator)
{
    struct Category category;
    return copy_range(first, last, output, allocator, category);
}

void dispatch_fill(void *first, unsigned count, const void *value, void *allocator)
{
    struct Category category;
    fill_count(first, count, value, allocator, category);
}
