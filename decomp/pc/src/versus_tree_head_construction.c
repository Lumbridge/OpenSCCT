struct Node { unsigned char color; struct Node *parent, *left, *right; };
struct Tree { struct Node *head; unsigned count; unsigned char compare; };
struct Args { const unsigned char *compare; const void *allocator; };
extern void *__stdcall allocate_node(unsigned bytes);
#define MAKE_TREE(Name, Size) \
    struct Tree *__fastcall Name(struct Tree *tree, struct Args args) \
    { \
        struct Node *head; \
        tree->head = 0; \
        head = allocate_node(Size); \
        tree->head = head; \
        tree->count = 0; \
        tree->compare = *args.compare; \
        head->color = 0; \
        tree->head->parent = 0; \
        tree->head->left = tree->head; \
        tree->head->right = tree->head; \
        return tree; \
    }

MAKE_TREE(init_tree_head_235f20, 24)
MAKE_TREE(init_tree_head_239ff0, 48)
