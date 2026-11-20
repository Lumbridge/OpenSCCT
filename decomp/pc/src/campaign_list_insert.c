struct List;
struct Node { struct Node *next, *previous; };

/* The second register argument represents the unused EDX slot of the
   original ECX receiver convention. All node arguments travel on the stack. */
extern struct Node *__fastcall construct_node(struct List *, unsigned unused_edx,
                                               struct Node *next, struct Node *previous,
                                               const void *value);
extern void __fastcall grow_list(struct List *, float count_bits);

void __fastcall insert_before(struct List *list, unsigned unused_edx,
                              struct Node *position, const void *value)
{
    struct Node *node = construct_node(list, unused_edx, position, position->previous, value);
    /* Pass the integer increment of one in a stack slot. */
    grow_list(list, 1.40129846e-45f);
    position->previous = node;
    node->previous->next = node;
}
