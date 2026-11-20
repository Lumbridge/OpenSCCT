struct Node { unsigned char color; struct Node *parent, *left, *right; };
struct Tree { struct Node *head; unsigned count; };
struct Position { struct Node *node; };
struct Output { struct Position *out; };
#define TREE_EDGE(Name, Value) \
    struct Position *__fastcall Name(struct Tree *tree, struct Output result) \
    { \
        result.out->node = Value; \
        return result.out; \
    }
TREE_EDGE(tree_begin_235f00, tree->head->left)
