struct StaticRecord { const void *methods; };

#define CLEANUP(Entry, Object, Methods) \
void Entry(void) \
{ \
    Object.methods = Methods; \
}
