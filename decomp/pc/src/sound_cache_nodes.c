#include "sound_cache.h"

#pragma code_seg(".text$V75cc70")
Node *__fastcall allocate_cache_node(List *list, NodeArg previous, NodeArg next)
{
    return make_node(list, previous.ptr, next.ptr);
}

#pragma code_seg(".text$V75ccf0")
void __fastcall free_cache_node(List *list, NodeArg ref)
{
    free_node(list, ref.ptr);
}

#pragma code_seg(".text$V75ccb0")
Node *__fastcall prepend_cache_item(List *list, ItemArg item)
{
    return add_first(list, item.ptr);
}

#pragma code_seg(".text$V75cd70")
__declspec(noinline) Node *__fastcall append_cache_item(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

#pragma code_seg(".text$V75cd30")
SoundCacheItem *__fastcall pop_last_cache_item(List *list)
{
    return pop_last(list);
}

#pragma code_seg(".text$V75cea0")
SoundCacheItem *__fastcall pop_first_cache_item(List *list)
{
    return pop_first(list);
}

#pragma code_seg(".text$V75cdb0")
__declspec(noinline) void __fastcall unlink_cache_node(List *list, NodeArg ref)
{
    Node *node = ref.ptr;
    if (node == list->first)
        list->first = node->next;
    else
        node->previous->next = node->next;
    if (node == list->last)
        list->last = node->previous;
    else
        node->next->previous = node->previous;
    free_node(list, node);
}

#pragma code_seg(".text$V75cdf0")
__declspec(noinline) Node *__fastcall insert_before_cache_node(List *list, NodeArg at, ItemArg item)
{
    Node *before = at.ptr, *node;
    if (!before)
        return add_first(list, item.ptr);
    node = make_node(list, before->previous, before);
    node->item = item.ptr;
    if (before->previous)
        before->previous->next = node;
    else
        list->first = node;
    before->previous = node;
    return node;
}

#pragma code_seg(".text$V75ce70")
Node *__fastcall find_cache_item(List *list, ItemArg item, NodeArg after)
{
    return find_item(list, item.ptr, after.ptr);
}

#pragma code_seg(".text$V75cc60")
SoundCacheItem **__fastcall next_cache_item_ref(List *list, CursorArg cursor)
{
    Node *node = *cursor.ptr;
    *cursor.ptr = node->next;
    return &node->item;
}
