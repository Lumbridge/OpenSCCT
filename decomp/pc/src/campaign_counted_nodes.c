struct Pool { unsigned reserved[2], count; };
struct Node { struct Node *next, *previous; };

extern void *__stdcall allocate_node(unsigned bytes);
extern void __stdcall release_node(void *node);

#define ALLOCATE_NODE(Size) \
    struct Node *__fastcall allocate_##Size(struct Pool *pool, unsigned unused_edx, \
                                            struct Node *previous, struct Node *next) \
    { \
        struct Node *node = allocate_node(Size); \
        if (node) { \
            node->next = next; \
            node->previous = previous; \
            ++pool->count; \
        } \
        return node; \
    }

void __fastcall release_counted_node(struct Pool *pool, unsigned unused_edx, struct Node *node)
{
    if (node) {
        release_node(node);
        --pool->count;
    }
}

ALLOCATE_NODE(12)
ALLOCATE_NODE(16)
ALLOCATE_NODE(40)
