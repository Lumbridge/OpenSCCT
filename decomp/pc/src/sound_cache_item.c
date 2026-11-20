#include "sound_cache.h"

#pragma code_seg(".text$V75d280")
__declspec(noinline) unsigned __fastcall cache_item_bytes(SoundCacheItem *item)
{
    return item->bytes;
}

#pragma code_seg(".text$V75d250")
__declspec(noinline) int __fastcall cache_item_priority(SoundCacheItem *item)
{
    return item->priority;
}

#pragma code_seg(".text$V75d190")
__declspec(noinline) int __fastcall cache_item_matches(SoundCacheItem *item, WordArg key)
{
    return key.value == item->key;
}
