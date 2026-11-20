#ifndef SOUND_CACHE_H
#define SOUND_CACHE_H

typedef struct SoundCacheItem
{
    unsigned fields00[2];
    unsigned bytes;
    int priority;
    unsigned key;
} SoundCacheItem;
typedef struct Node
{
    struct Node *next, *previous;
    SoundCacheItem *item;
} Node;
typedef struct
{
    Node *first, *last;
    unsigned count;
} List;
typedef struct
{
    int enabled;
    unsigned capacity, used;
    List entries;
} Cache;

/* MSVC x86 passes aggregate arguments on the stack and the fastcall receiver in ECX. */
typedef struct
{
    Node *ptr;
} NodeArg;
typedef struct
{
    SoundCacheItem *ptr;
} ItemArg;
typedef struct
{
    Node **ptr;
} CursorArg;
extern void *__stdcall SND_fn_pvMallocSnd(unsigned size);
extern void __stdcall SND_fn_vFreeSnd(void *ptr);

static __forceinline Node *make_node(List *list, Node *previous, Node *next)
{
    Node *node = SND_fn_pvMallocSnd(sizeof(Node));
    if (node)
    {
        node->next = next;
        node->previous = previous;
        ++list->count;
    }
    return node;
}
static __forceinline void free_node(List *list, Node *node)
{
    if (node)
    {
        SND_fn_vFreeSnd(node);
        --list->count;
    }
}
static __forceinline Node *add_first(List *list, SoundCacheItem *item)
{
    Node *node = make_node(list, 0, list->first);
    node->item = item;
    if (list->first)
        list->first->previous = node;
    else
        list->last = node;
    return list->first = node;
}
static __forceinline Node *add_last(List *list, SoundCacheItem *item)
{
    Node *node = make_node(list, list->last, 0);
    node->item = item;
    if (list->last)
        list->last->next = node;
    else
        list->first = node;
    return list->last = node;
}
static __forceinline SoundCacheItem *pop_first(List *list)
{
    Node *node = list->first;
    SoundCacheItem *item = node->item;
    list->first = node->next;
    if (list->first)
        list->first->previous = 0;
    else
        list->last = 0;
    free_node(list, node);
    return item;
}
static __forceinline SoundCacheItem *pop_last(List *list)
{
    Node *node = list->last;
    SoundCacheItem *item = node->item;
    list->last = node->previous;
    if (list->last)
        list->last->next = 0;
    else
        list->first = 0;
    free_node(list, node);
    return item;
}
static __forceinline Node *find_item(List *list, SoundCacheItem *item, Node *after)
{
    Node *node;
    if (!after)
        node = list->first;
    else
        node = after->next;
    while (node)
    {
        if (node->item == item)
            return node;
        node = node->next;
    }
    return 0;
}
static __forceinline SoundCacheItem *next_item(Node **cursor)
{
    Node *node = *cursor;
    *cursor = node->next;
    return node->item;
}

typedef struct
{
    unsigned value;
} WordArg;
__declspec(noinline) unsigned __fastcall cache_item_bytes(SoundCacheItem *item);
__declspec(noinline) int __fastcall cache_item_priority(SoundCacheItem *item);
__declspec(noinline) int __fastcall cache_item_matches(SoundCacheItem *item, WordArg key);
__declspec(noinline) Node *__fastcall append_cache_item(List *list, ItemArg item);
__declspec(noinline) void __fastcall unlink_cache_node(List *list, NodeArg ref);
__declspec(noinline) Node *__fastcall insert_before_cache_node(List *list, NodeArg at,
                                                               ItemArg item);

#endif
