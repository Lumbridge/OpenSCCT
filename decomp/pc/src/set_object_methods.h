/* The referenced read-only tables have executable method entries. */
struct ObjectHeader { const void *methods; };

#define RESET_METHODS(Entry, Methods) \
    void __fastcall Entry(struct ObjectHeader *object) { object->methods = Methods; }
