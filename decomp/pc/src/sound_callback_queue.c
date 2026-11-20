typedef void (__stdcall *Callback)(void *context);
typedef struct { Callback invoke; void *context; } Action;
typedef struct Node {
    struct Node *next, *previous;
    Action action;
} Node;
typedef struct { Node *first, *last; unsigned count; } Queue;
extern Queue MixerCallbacks;
extern void *__stdcall SND_fn_pvMallocSnd(unsigned bytes);
extern void __stdcall SND_fn_vFreeSnd(void *ptr);

static __forceinline Action pop_action(Queue *q)
{
    Node *item = q->first;
    Action saved = item->action;
    q->first = item->next;
    if (q->first)
        q->first->previous = 0;
    else
        q->last = 0;
    SND_fn_vFreeSnd(item);
    --q->count;
    return saved;
}
static __forceinline void add_action(Queue *q, Action action)
{
    Node *last = q->last;
    Node *item = SND_fn_pvMallocSnd(sizeof(Node));
    if (item) {
        item->next = 0;
        item->previous = last;
        ++q->count;
    }
    item->action = action;
    if (q->last)
        q->last->next = item;
    else
        q->first = item;
    q->last = item;
}
static __forceinline void drop_action(Queue *q, Node *item)
{
    if (item == q->first)
        q->first = item->next;
    else
        item->previous->next = item->next;
    if (item == q->last)
        q->last = item->previous;
    else
        item->next->previous = item->previous;
    SND_fn_vFreeSnd(item);
    --q->count;
}
#pragma code_seg(".text$V759460")
Action *__fastcall pop_mixer_action(Queue *q, int unused, Action *out)
{
    *out = pop_action(q);
    return out;
}
#pragma code_seg(".text$V7594a0")
void __fastcall insert_mixer_action(Queue *q, int unused, Action value)
{
    add_action(q, value);
}
#pragma code_seg(".text$V7594f0")
void __fastcall remove_mixer_action(Queue *q, int unused, Node *item)
{
    drop_action(q,item);
}
#pragma code_seg(".text$V759530")
void __fastcall discard_mixer_actions(Queue *q)
{
    while (q->count)
        pop_action(q);
}
#pragma code_seg(".text$V759570")
void __cdecl queue_mixer_callback(Callback callback, void *context)
{
    Action action;
    action.invoke = callback;
    action.context = context;
    add_action(&MixerCallbacks, action);
}
#pragma code_seg(".text$V7595d0")
void __cdecl run_mixer_callbacks(void)
{
    while (MixerCallbacks.count) {
        Action a = pop_action(&MixerCallbacks);
        a.invoke(a.context);
    }
}

#pragma code_seg()

/* Additional stock list instantiations. The node operations copy or discard
   the payload without interpreting it. */

void __fastcall list_remove_node_r467f80(Queue *q, int unused, Node *item)
{
    drop_action(q, item);
}

void __fastcall list_clear_nodes_r467ff0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_remove_node_r4690e0(Queue *q, int unused, Node *item)
{
    drop_action(q, item);
}

void __fastcall list_remove_node_r469240(Queue *q, int unused, Node *item)
{
    drop_action(q, item);
}

void __fastcall list_clear_nodes_r46a990(Queue *q)
{
    while (q->count)
        pop_action(q);
}

Action * __fastcall list_pop_pair_r46d0d0(Queue *q, int unused, Action *out)
{
    *out = pop_action(q);
    return out;
}

void __fastcall list_clear_nodes_r46e780(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_append_pair_r46e800(Queue *q, int unused, Action value)
{
    add_action(q, value);
}

void __fastcall list_append_pair_r46e910(Queue *q, int unused, Action value)
{
    add_action(q, value);
}

void __fastcall list_remove_node_r46e960(Queue *q, int unused, Node *item)
{
    drop_action(q, item);
}

void __fastcall list_clear_nodes_r46ea40(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_remove_node_r46eb00(Queue *q, int unused, Node *item)
{
    drop_action(q, item);
}

Action * __fastcall list_pop_pair_r46ebf0(Queue *q, int unused, Action *out)
{
    *out = pop_action(q);
    return out;
}

void __fastcall list_clear_nodes_r46ec70(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r46ecb0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r46ecf0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r46feb0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r46fef0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r46ff30(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r46ffb0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r46fff0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r4768f0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r477700(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r47ba40(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r47ba80(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r47bac0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r74c0a0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_remove_node_r74c0e0(Queue *q, int unused, Node *item)
{
    drop_action(q, item);
}

void __fastcall list_remove_node_r74c1e0(Queue *q, int unused, Node *item)
{
    drop_action(q, item);
}

void __fastcall list_clear_nodes_r74c250(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r74dea0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r74df70(Queue *q)
{
    while (q->count)
        pop_action(q);
}

Action * __fastcall list_pop_pair_r74e020(Queue *q, int unused, Action *out)
{
    *out = pop_action(q);
    return out;
}

void __fastcall list_clear_nodes_r74e0b0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

Action * __fastcall list_pop_pair_r74e130(Queue *q, int unused, Action *out)
{
    *out = pop_action(q);
    return out;
}

void __fastcall list_clear_nodes_r74e1c0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r753420(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_remove_node_r761430(Queue *q, int unused, Node *item)
{
    drop_action(q, item);
}

void __fastcall list_clear_nodes_r7614a0(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_remove_node_r7666e0(Queue *q, int unused, Node *item)
{
    drop_action(q, item);
}

void __fastcall list_remove_node_r7667e0(Queue *q, int unused, Node *item)
{
    drop_action(q, item);
}

void __fastcall list_clear_nodes_r766820(Queue *q)
{
    while (q->count)
        pop_action(q);
}

Action * __fastcall list_pop_pair_r766860(Queue *q, int unused, Action *out)
{
    *out = pop_action(q);
    return out;
}

void __fastcall list_clear_nodes_r766f00(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r767530(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r76d510(Queue *q)
{
    while (q->count)
        pop_action(q);
}

void __fastcall list_clear_nodes_r76d550(Queue *q)
{
    while (q->count)
        pop_action(q);
}
