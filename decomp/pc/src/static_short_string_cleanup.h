struct ShortString {
    union { char local[16]; char *heap; } storage;
    unsigned int length, capacity;
};
extern void __cdecl release_engine_buffer(void *buffer);

#define CLEANUP(Entry, Object) \
void Entry(void) \
{ \
    if (Object.capacity >= sizeof(Object.storage.local)) { \
        release_engine_buffer(Object.storage.heap); \
    } \
    Object.capacity = sizeof(Object.storage.local) - 1; \
    Object.length = 0; \
    Object.storage.local[0] = 0; \
}
