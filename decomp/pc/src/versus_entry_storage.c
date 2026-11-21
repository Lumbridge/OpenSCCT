struct Range { void *first, *end, *limit; };
struct List { unsigned allocator; struct Range range; };
struct Entry { unsigned char prefix[12]; struct Range range; unsigned char suffix[8]; };
struct EntryList { unsigned allocator; struct Entry *first, *end, *limit; };

extern void __cdecl release_memory(void *pointer);

static __forceinline void clear_range(struct Range *range)
{
    if (range->first) release_memory(range->first);
    range->first = 0;
    range->end = 0;
    range->limit = 0;
}
void __fastcall clear_list(struct List *list)
{
    clear_range(&list->range);
}
__declspec(noinline) void __fastcall clear_entry(struct Entry *entry)
{
    clear_range(&entry->range);
}
void __fastcall clear_entries(struct EntryList *list, unsigned unused,
                              struct Entry *first, struct Entry *end)
{
    while (first != end) {
        clear_entry(first);
        ++first;
    }
}
void __fastcall clear_entry_list(struct EntryList *list)
{
    if (list->first) {
        struct Entry *first = list->first;
        struct Entry *end = list->end;
        while (first != end) {
            clear_entry(first);
            ++first;
        }
        release_memory(list->first);
    }
    list->first = 0;
    list->end = 0;
    list->limit = 0;
}
