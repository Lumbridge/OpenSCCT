struct Array { void *buffer; int count, capacity; };
struct Archive;
struct Word { int value; };
struct ArchiveArg { struct Archive *ptr; };
struct Methods {
    void *reserved[5];
    void (__fastcall *count_bytes)(struct Archive *, int, int, int);
};
struct Archive { const struct Methods *methods; };
void __fastcall count_storage(struct Array *array, struct ArchiveArg archive, struct Word width)
{
    struct Word used, allocated;
    allocated.value = array->capacity * width.value;
    used.value = array->count * width.value;
    archive.ptr->methods->count_bytes(archive.ptr, width.value, used.value, allocated.value);
}
