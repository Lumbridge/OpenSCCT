struct Array { void *data; int size; unsigned capacity; };
struct Flags { unsigned value; };
struct Manager;
struct Methods {
    void *reserved;
    void (__fastcall *free_block)(struct Manager *, const struct Methods *, void *);
    void *more[3];
    void (__fastcall *release)(struct Manager *, const struct Methods *, void *, unsigned, unsigned);
};
struct Manager { const struct Methods *methods; };
extern struct Manager *manager;
static __forceinline void release_array(void *self, unsigned offset, unsigned element_bytes, unsigned category)
{
    struct Array *array = (struct Array *)((unsigned char *)self + offset);
    unsigned capacity = array->capacity;
    if (!(capacity & 0x80000000u))
        manager->methods->release(manager, manager->methods, array->data, (capacity & 0x7fffffffu) * element_bytes, category);
}
static __forceinline void *finish(void *self, struct Flags flags)
{
    if (flags.value & 1)
        manager->methods->free_block(manager, manager->methods, self);
    return self;
}

#define RELEASE_1_ARRAYS(Entry, Offset1, Stride1, Category1) \
    void *__fastcall Entry(void *self, struct Flags flags) \
    { \
        release_array(self, Offset1, Stride1, Category1); \
        return finish(self, flags); \
    }

#define RELEASE_2_ARRAYS(Entry, Offset1, Stride1, Category1, Offset2, Stride2, Category2) \
    void *__fastcall Entry(void *self, struct Flags flags) \
    { \
        release_array(self, Offset1, Stride1, Category1); \
        release_array(self, Offset2, Stride2, Category2); \
        return finish(self, flags); \
    }

RELEASE_1_ARRAYS(destroy_507370, 0, 4, 18)
RELEASE_2_ARRAYS(destroy_53c0b0, 20, 16, 18, 8, 16, 18)
RELEASE_2_ARRAYS(destroy_53c4d0, 16, 4, 18, 4, 4, 18)
RELEASE_1_ARRAYS(destroy_55aec0, 12, 8, 18)
RELEASE_2_ARRAYS(destroy_578d40, 12, 4, 18, 0, 64, 18)
RELEASE_1_ARRAYS(destroy_5792c0, 0, 4, 18)
RELEASE_2_ARRAYS(destroy_579630, 12, 4, 18, 0, 4, 18)
RELEASE_1_ARRAYS(destroy_579890, 0, 4, 18)
RELEASE_1_ARRAYS(destroy_599920, 80, 4, 18)
RELEASE_2_ARRAYS(destroy_599fc0, 36, 4, 18, 24, 4, 18)
RELEASE_1_ARRAYS(destroy_59a230, 12, 1, 18)
RELEASE_1_ARRAYS(destroy_6fde00, 4, 16, 18)
RELEASE_1_ARRAYS(destroy_6fdf50, 4, 16, 18)
RELEASE_1_ARRAYS(destroy_718b90, 8, 16, 18)
RELEASE_1_ARRAYS(destroy_71d1e0, 12, 8, 18)
