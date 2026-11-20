struct Registry;
struct Record { const void *methods; unsigned int unknown; void *storage; };
struct RecordArg { struct Record *record; };
extern struct Registry records;
extern void __fastcall unregister_record(struct Registry *, struct RecordArg);
extern void *__cdecl get_release_context(void);
extern void __cdecl release_record_storage(void *context, void *storage, unsigned type);
extern void __fastcall finish_record(struct Record *record);

#define FINALIZE(Entry, Object, Methods) \
    void Entry(void) \
    { \
        struct RecordArg argument; \
        argument.record = &Object; \
        Object.methods = Methods; \
        unregister_record(&records, argument); \
        if (Object.storage) { \
            release_record_storage(get_release_context(), Object.storage, 10); \
            Object.storage = 0; \
        } \
        finish_record(&Object); \
    }

extern const void *const methods_83c530[];
extern const void *const methods_83c548[];
extern const void *const methods_83c698[];
extern const void *const methods_83df68[];
extern const void *const methods_83e074[];
extern const void *const methods_83fe3c[];
extern const void *const methods_83fe68[];
extern const void *const methods_845238[];
extern const void *const methods_848c20[];
extern const void *const methods_8494d0[];
extern struct Record record_a1a5e0;
extern struct Record record_a1a604;
extern struct Record record_a1a630;
extern struct Record record_a1b0fc;
extern struct Record record_a1b13c;
extern struct Record record_a1b1cc;
extern struct Record record_a1b1e0;
extern struct Record record_a1b2b0;
extern struct Record record_a1b370;
extern struct Record record_a1b3b0;

FINALIZE(finalize_registry_record_7b4470, record_a1a5e0, methods_83c530)
FINALIZE(finalize_registry_record_7b44c0, record_a1a604, methods_83c548)
FINALIZE(finalize_registry_record_7b4520, record_a1a630, methods_83c698)
FINALIZE(finalize_registry_record_7b4620, record_a1b0fc, methods_83df68)
FINALIZE(finalize_registry_record_7b4680, record_a1b13c, methods_83e074)
FINALIZE(finalize_registry_record_7b4780, record_a1b1cc, methods_83fe3c)
FINALIZE(finalize_registry_record_7b47d0, record_a1b1e0, methods_83fe68)
FINALIZE(finalize_registry_record_7b4880, record_a1b2b0, methods_845238)
FINALIZE(finalize_registry_record_7b49a0, record_a1b370, methods_848c20)
FINALIZE(finalize_registry_record_7b4a10, record_a1b3b0, methods_8494d0)
