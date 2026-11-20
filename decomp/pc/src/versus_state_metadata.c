typedef unsigned short WChar;
struct NameEntry { unsigned header[3]; WChar text[1]; };
struct State {
    unsigned char reserved_00[0x20];
    unsigned name_index;
    unsigned reserved_24;
    struct State *super;
    unsigned char reserved_2c[0x0c];
    unsigned stored_name;
    void *script;
    unsigned script_size;
    unsigned char reserved_44[0x40];
    void *lookup;
};
struct Block { void *ptr; };
struct Heap;
struct HeapMethods {
    void *reserved[2];
    void (__fastcall *release)(struct Heap *, struct Block);
};
struct Heap { const struct HeapMethods *methods; };
extern struct Heap *engine_heap;
extern struct NameEntry **names;
extern const WChar struct_format[];
extern WChar name_buffer[];
extern int __cdecl format_name(WChar *, const WChar *, ...);
extern void __fastcall initialize_base(struct State *);
extern void __fastcall prepare_super(struct State *);
extern void __fastcall clean_base(struct State *);

const WChar *__fastcall get_struct_name(struct State *state)
{
    format_name(name_buffer, struct_format, names[state->name_index]->text);
    return name_buffer;
}

void __fastcall initialize_state_name(struct State *state)
{
    initialize_base(state);
    if (state->super)
        prepare_super(state->super);
    state->stored_name = state->name_index;
}

static __forceinline void release_storage(void **storage, void *value)
{
    if (value) {
        struct Block block;
        block.ptr = value;
        engine_heap->methods->release(engine_heap, block);
        *storage = 0;
    }
}

void __fastcall cleanup_state(struct State *state)
{
    void *script = state->script;
    state->script_size = 0;
    release_storage(&state->script, script);
    clean_base(state);
    release_storage(&state->lookup, state->lookup);
}
