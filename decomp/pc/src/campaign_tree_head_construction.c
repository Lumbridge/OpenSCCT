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

MAKE_TREE(init_tree_head_5ed260, 24)
MAKE_TREE(init_tree_head_5ed2c0, 24)
MAKE_TREE(init_tree_head_5ed320, 24)
MAKE_TREE(init_tree_head_5ed380, 24)
MAKE_TREE(init_tree_head_604430, 24)
MAKE_TREE(init_tree_head_6044c0, 48)
MAKE_TREE(init_tree_head_628730, 24)
MAKE_TREE(init_tree_head_62c1e0, 48)
MAKE_TREE(init_tree_head_62c240, 24)
MAKE_TREE(init_tree_head_62c290, 24)
MAKE_TREE(init_tree_head_62c2e0, 24)
MAKE_TREE(init_tree_head_722fc0, 24)
