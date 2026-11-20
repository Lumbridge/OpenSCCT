struct Watermark {
    unsigned levels[3];
    const unsigned short *name;
    unsigned timestamp;
    unsigned interval;
    float value;
    unsigned reserved;
    float scale;
};
struct Label { const unsigned short *name; };
struct Interval { unsigned value; };
struct Node {
    struct Node *next, *previous;
    struct Watermark *member;
};
struct Registry {
    unsigned reserved;
    struct Node *head;
    unsigned count;
};
struct Context;
extern struct Context *__cdecl get_context(void);
extern void __cdecl release_block(struct Context *, void *, unsigned);
struct MemberRef { struct Watermark **ptr; };
void __fastcall remove_member(struct Registry *list, struct MemberRef reference)
{
    struct Node *end = list->head;
    struct Node *node = end->next;
    while (node != end) {
        if (node->member == *reference.ptr) {
            struct Node *next = node->next;
            if (node != list->head) {
                node->previous->next = next;
                node->next->previous = node->previous;
                release_block(get_context(), node, 7);
                --list->count;
            }
            node = next;
        } else {
            node = node->next;
        }
    }
}
extern void __fastcall reset_member(struct Watermark *, float);
extern void __cdecl register_member(struct Watermark *);

extern void __cdecl unregister_member(struct Watermark *);
void __fastcall destroy_member(struct Watermark *member)
{
    unregister_member(member);
}

struct Watermark *__fastcall initialize_member(struct Watermark *member,
                                               struct Label label,
                                               struct Interval interval)
{

    member->name = label.name;
    member->interval = interval.value;
    reset_member(member, 0.0f);
    register_member(member);
    return member;
}
