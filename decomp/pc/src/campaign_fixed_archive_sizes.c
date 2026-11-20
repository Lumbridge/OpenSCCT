struct Array { void *buffer; int count, capacity; };
struct Archive;
struct Word { int value; };
struct ArchiveArg { struct Archive *ptr; };
struct Methods {
    void *reserved[5];
    void (__fastcall *count_bytes)(struct Archive *, const struct Methods *, int, int);
};
struct Archive { const struct Methods *methods; };
#define COUNT(Entry, Width) \
void __fastcall Entry(struct Array *array, struct ArchiveArg archive) \
{ \
    struct Word used, allocated; \
    allocated.value = array->capacity * Width; \
    used.value = array->count * Width; \
    archive.ptr->methods->count_bytes(archive.ptr, archive.ptr->methods, used.value, allocated.value); \
}

COUNT(count_0684f0, 2)
COUNT(count_073770, 28)
COUNT(count_073790, 16)
COUNT(count_076210, 4)
COUNT(count_076630, 1)
COUNT(count_07d220, 16)
COUNT(count_07d2d0, 8)
COUNT(count_07d320, 16)
COUNT(count_07fe00, 16)
COUNT(count_07fe20, 4)
COUNT(count_07feb0, 28)
COUNT(count_07ff30, 44)
COUNT(count_07ff50, 128)
COUNT(count_07ff70, 8)
COUNT(count_07ff90, 124)
COUNT(count_080110, 4)
COUNT(count_080190, 28)
COUNT(count_08afd0, 64)
COUNT(count_0ab930, 4)
COUNT(count_0b7af0, 28)
COUNT(count_0b7b10, 28)
COUNT(count_0b84d0, 132)
COUNT(count_0d7c40, 16)
COUNT(count_0d7c90, 120)
COUNT(count_0d7cb0, 136)
COUNT(count_0d7ce0, 8)
COUNT(count_0d7d40, 4)
COUNT(count_0d7d90, 16)
COUNT(count_0d7de0, 32)
COUNT(count_0d7e00, 8)
COUNT(count_0d7e20, 4)
COUNT(count_0d7e40, 16)
COUNT(count_0d7e60, 16)
COUNT(count_0d7eb0, 64)
COUNT(count_0d7ed0, 16)
COUNT(count_0d7ef0, 4)
COUNT(count_0d7f10, 16)
COUNT(count_0d7f30, 28)
COUNT(count_0d7f50, 8)
COUNT(count_0d7f70, 76)
COUNT(count_0d7fc0, 16)
COUNT(count_0d8010, 60)
COUNT(count_0d8ef0, 4)
COUNT(count_0d8fc0, 16)
COUNT(count_0d9010, 16)
COUNT(count_0d90c0, 16)
COUNT(count_0d91a0, 136)
COUNT(count_0d9200, 8)
COUNT(count_0d9300, 16)
COUNT(count_0d97f0, 8)
COUNT(count_130d50, 4)
COUNT(count_1476e0, 4)
COUNT(count_14ea60, 16)
COUNT(count_14eae0, 16)
COUNT(count_14eb00, 28)
COUNT(count_14eb20, 16)
COUNT(count_14eb40, 8)
COUNT(count_154960, 28)
COUNT(count_15a320, 60)
COUNT(count_15a370, 28)
COUNT(count_15a390, 16)
COUNT(count_16c5e0, 4)
COUNT(count_16c630, 8)
COUNT(count_16c680, 8)
COUNT(count_1ac120, 56)
COUNT(count_1ce8f0, 4)
COUNT(count_1dfc30, 4)
COUNT(count_1e9d20, 8)
COUNT(count_1e9d80, 56)
COUNT(count_1e9de0, 16)
COUNT(count_371a70, 60)
COUNT(count_371af0, 64)
COUNT(count_3d8760, 4)
COUNT(count_402e50, 32)
COUNT(count_402ea0, 4)
