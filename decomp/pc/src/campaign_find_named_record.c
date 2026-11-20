struct NamedRecord;
struct RecordList { struct NamedRecord **begin, **end; };
struct Identifier { unsigned value; };

#define FIND_RECORD(Entry, Offset, NameOffset) \
    struct NamedRecord *__fastcall Entry(void *owner, struct Identifier id) \
    { \
        struct RecordList *list = (struct RecordList *)((char *)owner + Offset); \
        struct NamedRecord **entry = list->begin, **end = list->end; \
        while (entry != end) { \
            if (*(const unsigned *)((const char *)*entry + NameOffset) == id.value) return *entry; \
            ++entry; \
        } \
        return 0; \
    }

FIND_RECORD(find_421780, 8, 20)
FIND_RECORD(find_4217b0, 24, 4)
FIND_RECORD(find_430670, 8, 4)
