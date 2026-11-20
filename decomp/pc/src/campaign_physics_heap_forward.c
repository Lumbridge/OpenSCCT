struct Manager;
struct Methods {
    void *reserved;
    void (__fastcall *free_block)(struct Manager *, void *, void *);
    void *more[2];
    void *(__fastcall *allocate)(struct Manager *, unsigned, unsigned, unsigned);
    void (__fastcall *release)(struct Manager *, void *, void *, unsigned, unsigned);
};
struct Manager { const struct Methods *methods; };
extern struct Manager *manager;
#define ALLOCATE(Entry, Category) \
    void *__cdecl Entry(unsigned bytes) \
    { \
        return manager->methods->allocate(manager, bytes, bytes, Category); \
    }
#define RELEASE(Entry, Bytes, Category) \
    void __cdecl Entry(void *buffer) \
    { \
        manager->methods->release(manager, buffer, buffer, Bytes, Category); \
    }
#define FREE_BLOCK(Entry) \
    void __cdecl Entry(void *buffer) \
    { \
        manager->methods->free_block(manager, buffer, buffer); \
    }

FREE_BLOCK(entry_106e40)
ALLOCATE(entry_1b7640, 34)
ALLOCATE(entry_1b7750, 5)
FREE_BLOCK(entry_4fbe50)
RELEASE(entry_4fee40, 12, 25)
RELEASE(entry_4ff2f0, 16, 25)
ALLOCATE(entry_504480, 6)
RELEASE(entry_5044a0, 12, 6)
ALLOCATE(entry_5054f0, 23)
RELEASE(entry_505510, 24, 23)
RELEASE(entry_506080, 12, 23)
RELEASE(entry_506350, 12, 23)
FREE_BLOCK(entry_5064c0)
FREE_BLOCK(entry_5072d0)
FREE_BLOCK(entry_53e4f0)
FREE_BLOCK(entry_53f260)
ALLOCATE(entry_5507d0, 26)
RELEASE(entry_5507f0, 16, 26)
ALLOCATE(entry_550810, 26)
RELEASE(entry_550830, 32, 26)
ALLOCATE(entry_5509c0, 28)
ALLOCATE(entry_550b60, 34)
RELEASE(entry_550b80, 260, 34)
RELEASE(entry_579910, 12, 18)
ALLOCATE(entry_586670, 9)
RELEASE(entry_586690, 64, 9)
FREE_BLOCK(entry_58fa10)
RELEASE(entry_591fb0, 332, 18)
FREE_BLOCK(entry_6ee240)
FREE_BLOCK(entry_6f3210)
ALLOCATE(entry_6fd6f0, 34)
RELEASE(entry_791530, 8, 1)
