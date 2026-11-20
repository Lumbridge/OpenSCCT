struct NamedRecord;
struct Identifier { unsigned value; };
extern unsigned *__cdecl name_crc(unsigned *, const char *);
extern struct NamedRecord *__fastcall find_421780(void *, struct Identifier);
extern struct NamedRecord *__fastcall find_430670(void *, struct Identifier);

#define FIND_NAME(Entry, Find) \
    struct NamedRecord *__cdecl Entry(void *owner, const char *name) \
    { \
        unsigned value; \
        struct Identifier id; \
        id.value = *name_crc(&value, name); \
        return Find(owner, id); \
    }

FIND_NAME(find_name_1d3fe0, find_430670)
FIND_NAME(find_name_1d5340, find_430670)
FIND_NAME(find_name_1d5370, find_421780)
