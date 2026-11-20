union Key { unsigned u32; int s32; unsigned short u16; unsigned char u8; };
struct Node { unsigned char color; struct Node *parent, *left, *right; union Key key; };
#define ROTATE(Name, Near, Far) \
    static __forceinline void Name(struct Node *node, struct Node **root) \
    { \
        struct Node *pivot = node->Near; \
        node->Near = pivot->Far; \
        if (pivot->Far) pivot->Far->parent = node; \
        pivot->parent = node->parent; \
        if (node == *root) *root = pivot; \
        else if (node == node->parent->Far) node->parent->Far = pivot; \
        else node->parent->Near = pivot; \
        pivot->Far = node; \
        node->parent = pivot; \
    }
ROTATE(rotate_left, right, left)
ROTATE(rotate_right, left, right)
struct Tree { struct Node *head; unsigned count; };
struct Position { struct Node *node; };
struct Args {
    struct Position *out;
    struct Node *left_hint, *parent;
    const union Key *value;
    unsigned right_hint;
};
struct Value { const union Key *data; };

extern void rotate_left_call(struct Node *,struct Node **);
extern void rotate_right_call(struct Node *,struct Node **);
#define FIX_INSERT(Near, Far, First, Second) \
        struct Node *uncle = current->parent->parent->Far; \
        if (uncle && uncle->color == 0) { \
            current->parent->color = 1; \
            uncle->color = 1; \
            current->parent->parent->color = 0; \
            current = current->parent->parent; \
        } else { \
            if (current == current->parent->Far) { \
                current = current->parent; First(current, root); \
            } \
            current->parent->color = 1; \
            current->parent->parent->color = 0; \
            Second(current->parent->parent, root); \
        }

#define INSERT_NODE(Entry, Make, Field) \
    struct Position *__fastcall Entry(struct Tree *tree, struct Args args) \
    { \
        struct Value value; \
        struct Node *node, *parent = args.parent, *current; \
        struct Node **root; \
        value.data = args.value; \
        if (parent == tree->head || (!args.right_hint && (args.left_hint || value.data->Field < parent->key.Field))) { \
            node = Make(tree, value); \
            parent->left = node; \
            if (parent == tree->head) { \
                tree->head->parent = node; \
                tree->head->right = node; \
            } else if (parent == tree->head->left) tree->head->left = node; \
        } else { \
            node = Make(tree, value); \
            parent->right = node; \
            if (parent == tree->head->right) tree->head->right = node; \
        } \
        node->parent = parent; \
        node->left = 0; \
        node->right = 0; \
        current = node; \
        root = &tree->head->parent; \
        current->color = 0; \
        while (current != *root && current->parent->color == 0) { \
            if (current->parent == current->parent->parent->left) { \
                FIX_INSERT(left, right, rotate_left, rotate_right) \
            } else { \
                FIX_INSERT(right, left, rotate_right_call, rotate_left_call) \
            } \
        } \
        (*root)->color = 1; \
        ++tree->count; \
        args.out->node = node; \
        return args.out; \
    }
