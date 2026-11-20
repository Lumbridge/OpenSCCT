/* Versus installs the UnrealScript natives and primitive conversions at startup.
   The handler ABI uses ECX for the receiver and two stack arguments. */
struct ScriptObject;
struct ScriptFrame;
typedef void __fastcall VmHandler(struct ScriptObject *, unsigned int,
                                  struct ScriptFrame *, void *);

enum { NATIVE_COUNT = 4096, CAST_COUNT = 255 };
extern VmHandler undefined_native;
extern VmHandler *native_table[NATIVE_COUNT];
extern VmHandler *cast_table[CAST_COUNT];
extern int native_initialized, native_duplicate;
extern int cast_initialized, cast_duplicate;

static __forceinline void register_native(unsigned slot, VmHandler *handler)
{
    if (!native_initialized) {
        unsigned i;
        native_initialized = 1;
        for (i = 0; i < NATIVE_COUNT; ++i)
            native_table[i] = undefined_native;
    }
    if (native_table[slot] != undefined_native)
        native_duplicate = slot;
    native_table[slot] = handler;
}

static __forceinline void register_cast(unsigned slot, VmHandler *handler)
{
    if (!cast_initialized) {
        unsigned i;
        cast_initialized = 1;
        for (i = 0; i < CAST_COUNT; ++i)
            cast_table[i] = undefined_native;
    }
    if (cast_table[slot] != undefined_native)
        cast_duplicate = slot;
    cast_table[slot] = handler;
}

#define REGISTER_NATIVE(slot, name) \
    do { extern VmHandler name; register_native(slot, name); } while (0)
#define REGISTER_CAST(slot, name) \
    do { extern VmHandler name; register_cast(slot, name); } while (0)
