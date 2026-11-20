#include "sound_cache.h"

#pragma code_seg(".text$V75cbf0")
int __fastcall cache_accepts_size(Cache *cache, ItemArg item)
{
    if (!cache->enabled)
        return 0;
    return cache_item_bytes(item.ptr) <= cache->capacity / 2;
}

#pragma code_seg(".text$V75cee0")
int __fastcall cache_insert_priority(Cache *cache, ItemArg item)
{
    int priority;
    Node *cursor;
    if (!cache->enabled)
        return 0;
    priority = cache_item_priority(item.ptr);
    cursor = cache->entries.first;
    while (cursor)
    {
        NodeArg at;
        SoundCacheItem *other;
        at.ptr = cursor;
        other = next_item(&cursor);
        if (cache_item_priority(other) <= priority)
        {
            insert_before_cache_node(&cache->entries, at, item);
            cache->used += cache_item_bytes(item.ptr);
            return 1;
        }
    }
    append_cache_item(&cache->entries, item);
    cache->used += cache_item_bytes(item.ptr);
    return 1;
}

#pragma code_seg(".text$V75cf60")
void __fastcall cache_remove_item(Cache *cache, ItemArg item)
{
    NodeArg at;
    if (!cache->enabled)
        return;
    at.ptr = find_item(&cache->entries, item.ptr, 0);
    if (at.ptr)
    {
        unlink_cache_node(&cache->entries, at);
        cache->used -= cache_item_bytes(item.ptr);
    }
}

#pragma code_seg(".text$V75cfa0")
SoundCacheItem *__fastcall cache_pop_last(Cache *cache)
{
    SoundCacheItem *item;
    if (!cache->enabled)
        return 0;
    item = pop_last(&cache->entries);
    if (item)
        cache->used -= cache_item_bytes(item);
    return item;
}

#pragma code_seg(".text$V75cff0")
SoundCacheItem *__fastcall cache_take_key(Cache *cache, WordArg key)
{
    Node *cursor;
    if (!cache->enabled)
        return 0;
    cursor = cache->entries.first;
    while (cursor)
    {
        NodeArg at;
        SoundCacheItem *item;
        at.ptr = cursor;
        item = next_item(&cursor);
        if (cache_item_matches(item, key))
        {
            unlink_cache_node(&cache->entries, at);
            cache->used -= cache_item_bytes(item);
            return item;
        }
    }
    return 0;
}

#pragma code_seg(".text$V75d060")
__declspec(noinline) void __fastcall clear_cache_list(List *list)
{
    while (list->count)
        pop_first(list);
}
static __forceinline void initialize_list(List *list)
{
    list->count = 0;
    list->last = 0;
    list->first = 0;
}

#pragma code_seg(".text$V75d0b0")
Cache *__fastcall initialize_sound_cache(Cache *cache, WordArg capacity)
{
    initialize_list(&cache->entries);
    cache->enabled = 1;
    cache->capacity = capacity.value;
    clear_cache_list(&cache->entries);
    cache->used = 0;
    return cache;
}

#pragma code_seg(".text$V75d0f0")
void __fastcall release_sound_cache(Cache *cache)
{
    clear_cache_list(&cache->entries);
}
