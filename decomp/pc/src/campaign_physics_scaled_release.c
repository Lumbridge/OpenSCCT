struct Manager;
struct Pointer { void *ptr; };
struct Word { unsigned value; };
struct Methods {
    void *reserved[5];
    void (__fastcall *release)(struct Manager *, const struct Methods *, void *, unsigned, unsigned);
};
struct Manager { const struct Methods *methods; };
extern struct Manager *manager;

#define RELEASE_BUFFER(Entry, ElementBytes) \
    void __cdecl Entry(struct Pointer buffer, unsigned count, struct Word category) \
    { \
        manager->methods->release(manager, manager->methods, buffer.ptr, count * ElementBytes, category.value); \
    }

RELEASE_BUFFER(release_107120, 12u)
RELEASE_BUFFER(release_1d30d0, 144u)
RELEASE_BUFFER(release_4fcef0, 12u)
RELEASE_BUFFER(release_50a470, 48u)
RELEASE_BUFFER(release_5128b0, 12u)
RELEASE_BUFFER(release_5128e0, 160u)
RELEASE_BUFFER(release_514090, 48u)
RELEASE_BUFFER(release_54d260, 80u)
RELEASE_BUFFER(release_5522c0, 12u)
RELEASE_BUFFER(release_563cb0, 48u)
RELEASE_BUFFER(release_578aa0, 80u)
RELEASE_BUFFER(release_579a80, 12u)
RELEASE_BUFFER(release_592900, 20u)
RELEASE_BUFFER(release_59a8f0, 48u)
RELEASE_BUFFER(release_5a1540, 192u)
RELEASE_BUFFER(release_6fe580, 48u)
