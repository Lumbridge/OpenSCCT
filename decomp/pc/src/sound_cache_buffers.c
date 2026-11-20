typedef unsigned Word;
typedef struct
{
    Word value;
} WordArg;
typedef struct BufferObject BufferObject;
typedef struct
{
    BufferObject *value;
} BufferArg;
typedef struct
{
    void(__fastcall *destroy)(BufferObject *, float stack_flags);
} BufferVTable;
struct BufferObject
{
    const BufferVTable *methods;
    Word status, has_spatial;
};
typedef struct
{
    Word busy, reusable, bytes;
    int priority;
    void *key;
    BufferObject *primary, *secondary;
} Item;
typedef struct
{
    void *value;
} KeyArg;

extern long __fastcall set_buffer_position(BufferObject *, float);
extern void __stdcall SND_fn_vFreeSnd(void *);
extern void *__stdcall SND_fn_pvMallocSnd(unsigned);
/* A floating parameter keeps an integer flag on the stack in MSVC x86 fastcall.
   This supplies the single-bit release flag expected by the deleting destructor. */
static const union {
    Word bits;
    float stack_word;
} release_owned = {1};
static __forceinline void set_buffers(Item *item, BufferArg first, WordArg first_bytes,
                                      BufferArg second, WordArg second_bytes)
{
    item->primary = first.value;
    item->secondary = second.value;
    item->bytes = first_bytes.value + second_bytes.value;
}
static __forceinline void set_item(Item *item, WordArg reusable, KeyArg key, BufferArg first,
                                   WordArg first_bytes, BufferArg second, WordArg second_bytes)
{
    set_buffers(item, first, first_bytes, second, second_bytes);
    item->priority = 0;
    item->busy = 0;
    item->reusable = reusable.value;
    item->key = key.value;
}

#pragma code_seg(".text$V75d100")
void __fastcall configure_cache_item(Item *item, WordArg reusable, KeyArg key, BufferArg first,
                                     WordArg first_bytes, BufferArg second, WordArg second_bytes)
{
    set_item(item, reusable, key, first, first_bytes, second, second_bytes);
}

#pragma code_seg(".text$V75d140")
Item *__fastcall construct_empty_cache_item(Item *item)
{
    item->primary = 0;
    item->secondary = 0;
    item->bytes = 0;
    item->priority = 0;
    item->busy = 0;
    item->reusable = 1;
    item->key = 0;
    return item;
}

#pragma code_seg(".text$V75d160")
void __fastcall destroy_cache_item_buffers(Item *item)
{
    if (item->primary)
    {
        item->primary->methods->destroy(item->primary, release_owned.stack_word);
        item->primary = 0;
    }
    if (item->secondary)
    {
        item->secondary->methods->destroy(item->secondary, release_owned.stack_word);
        item->secondary = 0;
    }
}

#pragma code_seg(".text$V75d1b0")
int __fastcall cache_item_busy(Item *item)
{
    return item->busy;
}

#pragma code_seg(".text$V75d1c0")
int __fastcall cache_item_reusable(Item *item)
{
    return item->reusable;
}

#pragma code_seg(".text$V75d1d0")
void __fastcall mark_cache_item_busy(Item *item)
{
    if (!item->busy)
        item->busy = 1;
}

#pragma code_seg(".text$V75d1e0")
void __fastcall mark_cache_item_available(Item *item)
{
    if (item->busy)
        item->busy = 0;
}

#pragma code_seg(".text$V75d1f0")
void __fastcall rewind_cache_item_buffers(Item *item)
{
    if (item->primary)
    {
        set_buffer_position(item->primary, 0.0f);
    }
    if (item->secondary)
    {
        set_buffer_position(item->secondary, 0.0f);
    }
}

#pragma code_seg(".text$V75d220")
void *__cdecl allocate_cache_item(unsigned size)
{
    return SND_fn_pvMallocSnd(size);
}

#pragma code_seg(".text$V75d230")
void __cdecl free_cache_item(Item *item)
{
    SND_fn_vFreeSnd(item);
}

#pragma code_seg(".text$V75d240")
void __fastcall set_cache_priority(Item *item, WordArg priority)
{
    item->priority = priority.value;
}

#pragma code_seg(".text$V75d260")
void __fastcall attach_cache_buffers(Item *item, BufferArg first, WordArg first_bytes,
                                     BufferArg second, WordArg second_bytes)
{
    set_buffers(item, first, first_bytes, second, second_bytes);
}

#pragma code_seg(".text$V75d290")
BufferObject *__fastcall primary_cache_buffer(Item *item)
{
    return item->primary;
}

#pragma code_seg(".text$V75d2a0")
BufferObject *__fastcall secondary_cache_buffer(Item *item)
{
    return item->secondary;
}

#pragma code_seg(".text$V75d2b0")
Item *__fastcall construct_cache_item(Item *item, WordArg reusable, KeyArg key, BufferArg first,
                                      WordArg first_bytes, BufferArg second, WordArg second_bytes)
{
    set_item(item, reusable, key, first, first_bytes, second, second_bytes);
    return item;
}
