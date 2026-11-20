/* Tree head and node links use the same sentinel as leaf pointers. */
#define TREE_LAYOUT(N, Payload) \
    struct N { struct N *left, *parent, *right; unsigned char value[Payload]; unsigned char color, sentinel; }; \
    struct Tree_##N { void *allocator; struct N *head; }; \
    struct Ref_##N { struct N *node; }

#define ITERATE(Entry, N, Near, Far, Reverse) \
    void __fastcall Entry(struct Ref_##N *it) \
    { \
        struct N *node = it->node; \
        struct N *next; \
        if (node->sentinel) { \
            if (Reverse) it->node = node->right; \
        } else if (!node->Near->sentinel) { \
            next = node->Near; \
            while (!next->Far->sentinel) next = next->Far; \
            it->node = next; \
        } else { \
            next = node->parent; \
            while (!next->sentinel && it->node == next->Near) { \
                it->node = next; \
                next = next->parent; \
            } \
            if (!Reverse || !next->sentinel) it->node = next; \
        } \
    }
