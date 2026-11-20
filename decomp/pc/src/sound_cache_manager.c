typedef unsigned Word;
typedef struct
{
    Word value;
} WordArg;
typedef struct
{
    Word state[2], has_spatial;
} Buffer;
typedef struct Item Item;
typedef struct
{
    Item *ptr;
} ItemArg;
typedef struct
{
    Word flags[9], options, fields28[12];
    Item **items;
    Word count;
} Resource;
typedef struct
{
    Resource *ptr;
} ResourceArg;
typedef struct Cache Cache;
typedef struct
{
    char fields[0x1c];
    Cache *cache;
    char fields20[0x304];
    int priority;
} Manager;
extern Item *__fastcall cache_take_key(Cache *, ResourceArg);
extern Buffer *__fastcall primary_cache_buffer(Item *);
extern Buffer *__fastcall secondary_cache_buffer(Item *);
extern void __fastcall set_buffer_spatial_mode(Buffer *, float);
extern int __fastcall cache_item_busy(Item *);
extern int __fastcall cache_item_reusable(Item *);
extern void __fastcall mark_cache_item_busy(Item *);
extern void __fastcall mark_cache_item_available(Item *);
extern void __fastcall rewind_cache_item_buffers(Item *);
extern int __fastcall cache_accepts_size(Cache *, ItemArg);
extern Word __fastcall cache_limit(Cache *);
extern Word __fastcall cache_usage(Cache *);
extern Word __fastcall cache_item_bytes(Item *);
extern Item *__fastcall cache_pop_last(Cache *);
extern void __fastcall destroy_cache_item_buffers(Item *);
extern void __cdecl free_cache_item(Item *);
extern int __fastcall cache_insert_priority(Cache *, ItemArg);
extern void __fastcall set_cache_priority(Item *, WordArg);

static __forceinline void destroy(Item *item)
{
    if (item)
    {
        destroy_cache_item_buffers(item);
        free_cache_item(item);
    }
}
typedef struct Work Work;
typedef struct
{
    void *ptr;
} PtrArg;
extern PtrArg AudioTaskContext;
extern void *__cdecl allocate_work(unsigned size);
extern void __cdecl free_work(void *ptr);
extern Work *__fastcall initialize_acquisition(Work *, PtrArg context, ResourceArg, WordArg flags);
extern void __fastcall step_acquisition(Work *);
extern int __fastcall get_acquisition_state(Work *);
extern Item *__fastcall get_acquisition_item(Work *);

#pragma code_seg(".text$V752420")
__declspec(noinline)
Item *__fastcall create_managed_item(Manager *manager, ResourceArg resource, WordArg flags)
{
    Item *item = 0;
    Work *work = allocate_work(24);
    if (work)
    {

        int state;

        work = initialize_acquisition(work, AudioTaskContext, resource, flags);
        if (work)
        {
            do
            {
                step_acquisition(work);
                state = get_acquisition_state(work);
            } while (state == 2);
            if (state == 3)
                item = get_acquisition_item(work);
            free_work(work);
        }
    }
    return item;
}

#pragma code_seg(".text$V7503c0")
__declspec(noinline) Item *__fastcall claim_available_item(Manager *manager, ResourceArg resource)
{
    Word i;
    for (i = 0; i < resource.ptr->count; ++i)
    {
        Item *item = resource.ptr->items[i];
        if (!cache_item_busy(item))
        {
            mark_cache_item_busy(item);
            return item;
        }
    }
    return 0;
}

#pragma code_seg(".text$V750400")
Item *__fastcall take_cached_resource(Manager *manager, ResourceArg resource)
{
    Item *result = 0;
    if (manager->cache)
        result = cache_take_key(manager->cache, resource);
    return result;
}

#pragma code_seg(".text$V752490")
void __fastcall discard_managed_item(Manager *manager, ItemArg ref)
{
    destroy(ref.ptr);
}

#pragma code_seg(".text$V752550")
void __fastcall return_managed_item(Manager *manager, ItemArg ref)
{
    Item *item = ref.ptr;
    Word space;
    rewind_cache_item_buffers(item);
    if (cache_item_reusable(item))
    {
        mark_cache_item_available(item);
        return;
    }
    if (cache_accepts_size(manager->cache, ref))
    {
        WordArg priority;
        space = cache_limit(manager->cache);
        space -= cache_usage(manager->cache);
        while (space < cache_item_bytes(item))
        {
            destroy(cache_pop_last(manager->cache));
            space = cache_limit(manager->cache);
            space -= cache_usage(manager->cache);
        }
        priority.value = manager->priority;
        set_cache_priority(item, priority);
        cache_insert_priority(manager->cache, ref);
    }
    else
    {
        destroy(item);
    }
}

typedef struct
{
    Word leading[9], options;
} PlaybackOptions;
typedef struct
{
    PlaybackOptions *ptr;
} OptionsArg;
/* Bit-preserving 32-bit stack flag for the x86 member-call ABI. */
static const union {
    Word bits;
    float stack_word;
} enable_spatial = {1};
static __forceinline void apply_spatial_option(Buffer *buffer, const PlaybackOptions *options)
{
    if (buffer && buffer->has_spatial)
    {
        if (options->options & 1)
            set_buffer_spatial_mode(buffer, 0.0f);
        else
            set_buffer_spatial_mode(buffer, enable_spatial.stack_word);
    }
}

#pragma code_seg(".text$V7524b0")
Item *__fastcall acquire_managed_item(Manager *manager, ResourceArg resource, OptionsArg options)
{
    Item *item = claim_available_item(manager, resource);
    if (!item)
    {
        item = take_cached_resource(manager, resource);
        if (!item)
        {
            WordArg flags = {0};
            item = create_managed_item(manager, resource, flags);
        }
    }
    if (item)
    {
        Buffer *first = primary_cache_buffer(item);
        Buffer *second = secondary_cache_buffer(item);
        apply_spatial_option(first, options.ptr);
        apply_spatial_option(second, options.ptr);
    }
    return item;
}
