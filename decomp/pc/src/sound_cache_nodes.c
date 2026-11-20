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
static __forceinline Node *insert_before(List *list, NodeArg at, ItemArg item)
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

__declspec(noinline) Node *__fastcall insert_before_cache_node(List *list, NodeArg at, ItemArg item)
{
    return insert_before(list, at, item);
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

#pragma code_seg()

/* Additional stock list instantiations. The node operations copy or discard
   the payload without interpreting it. */

SoundCacheItem * __fastcall list_pop_first_pointer_r467eb0(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_last_pointer_r467ef0(List *list)
{
    return pop_last(list);
}

Node * __fastcall list_append_pointer_r469150(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

Node * __fastcall list_append_pointer_r469200(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r46a8c0(List *list)
{
    return pop_first(list);
}

Node * __fastcall list_prepend_pointer_r46cf60(List *list, ItemArg item)
{
    return add_first(list, item.ptr);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r46e680(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_last_pointer_r46e6c0(List *list)
{
    return pop_last(list);
}

Node * __fastcall list_prepend_pointer_r46e700(List *list, ItemArg item)
{
    return add_first(list, item.ptr);
}

Node * __fastcall list_append_pointer_r46e740(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

Node * __fastcall list_append_pointer_r46e7c0(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

Node * __fastcall list_append_pointer_r46e8a0(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r46ea80(List *list)
{
    return pop_first(list);
}

Node * __fastcall list_append_pointer_r46eac0(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r46eb70(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r46ebb0(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r46ec30(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r46ed30(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r46ed70(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r476870(List *list)
{
    return pop_first(list);
}

Node * __fastcall list_append_pointer_r4768b0(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r476930(List *list)
{
    return pop_first(list);
}

Node * __fastcall list_prepend_pointer_r479f40(List *list, ItemArg item)
{
    return add_first(list, item.ptr);
}

Node * __fastcall list_append_pointer_r479f80(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

Node * __fastcall list_prepend_pointer_r47a000(List *list, ItemArg item)
{
    return add_first(list, item.ptr);
}

Node * __fastcall list_append_pointer_r47a040(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

Node * __fastcall list_prepend_pointer_r47b310(List *list, ItemArg item)
{
    return add_first(list, item.ptr);
}

Node * __fastcall list_prepend_pointer_r47b370(List *list, ItemArg item)
{
    return add_first(list, item.ptr);
}

Node * __fastcall list_prepend_pointer_r47b3d0(List *list, ItemArg item)
{
    return add_first(list, item.ptr);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r47b740(List *list)
{
    return pop_first(list);
}

Node * __fastcall list_append_pointer_r47b780(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

Node * __fastcall list_insert_before_r47b7c0(List *list, NodeArg at, ItemArg item)
{
    return insert_before(list, at, item);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r47b840(List *list)
{
    return pop_first(list);
}

Node * __fastcall list_append_pointer_r47b880(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

Node * __fastcall list_insert_before_r47b8c0(List *list, NodeArg at, ItemArg item)
{
    return insert_before(list, at, item);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r47b940(List *list)
{
    return pop_first(list);
}

Node * __fastcall list_append_pointer_r47b980(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

Node * __fastcall list_insert_before_r47b9c0(List *list, NodeArg at, ItemArg item)
{
    return insert_before(list, at, item);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r74bfd0(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r74c150(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r74de10(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r74dee0(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r7533a0(List *list)
{
    return pop_first(list);
}

Node * __fastcall list_append_pointer_r7533e0(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r761120(List *list)
{
    return pop_first(list);
}

Node * __fastcall list_prepend_pointer_r7613f0(List *list, ItemArg item)
{
    return add_first(list, item.ptr);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r766470(List *list)
{
    return pop_first(list);
}

Node * __fastcall list_append_pointer_r7666a0(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r767480(List *list)
{
    return pop_first(list);
}

Node * __fastcall list_append_pointer_r7674c0(List *list, ItemArg item)
{
    return add_last(list, item.ptr);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r76d3b0(List *list)
{
    return pop_first(list);
}

SoundCacheItem * __fastcall list_pop_first_pointer_r76d480(List *list)
{
    return pop_first(list);
}
