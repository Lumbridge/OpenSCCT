struct Node { struct Node *next, *previous; unsigned value; };

struct Node *__fastcall initialize_node(struct Node *node, unsigned unused_edx,
                                        struct Node *next, struct Node *previous,
                                        const unsigned *value)
{
    node->next = next;
    node->previous = previous;
    node->value = *value;
    return node;
}
