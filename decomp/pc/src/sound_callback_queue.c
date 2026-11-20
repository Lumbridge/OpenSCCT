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
