struct Node {
    struct Node *left, *parent, *right;
    unsigned value, color;
};
struct Tree { unsigned reserved; struct Node *head; };
struct NodeRef { struct Node *ptr; };
struct Color { unsigned value; };
extern struct Node *nil;
extern void *allocate_node(unsigned size);

struct Node *tree_minimum(struct Node *node)
{
    while (node->left != nil) node = node->left;
    return node;
}

struct Node *__fastcall make_node(struct Tree *tree, struct NodeRef parent, struct Color color)
{
    struct Node *node = allocate_node(sizeof(*node));
    node->parent = parent.ptr;
    node->color = color.value;
    return node;
}

void __fastcall rotate_left(struct Tree *tree, struct NodeRef root)
{
    struct Node *x = root.ptr;
    struct Node *y = x->right;
    x->right = y->left;
    if (y->left != nil) y->left->parent = x;
    y->parent = x->parent;
    if (x == tree->head->parent) tree->head->parent = y;
    else if (x == x->parent->left) x->parent->left = y;
    else x->parent->right = y;
    y->left = x;
    x->parent = y;
}

void __fastcall rotate_right(struct Tree *tree, struct NodeRef root)
{
    struct Node *x = root.ptr;
    struct Node *y = x->left;
    x->left = y->right;
    if (y->right != nil) y->right->parent = x;
    y->parent = x->parent;
    if (x == tree->head->parent) tree->head->parent = y;
    else if (x == x->parent->right) x->parent->right = y;
    else x->parent->left = y;
    y->right = x;
    x->parent = y;
}

void __stdcall fill_dwords(unsigned *out, unsigned count, const unsigned *source)
{
    for (; count > 0; --count, ++out) {
        if (out) *out = *source;
    }
}

#define DEFINE_COPY(Type, Name) \
    Type *copy_##Name(const Type *first, const Type *last, Type *out) \
    { \
        while (first != last) *out++ = *first++; \
        return out; \
    } \
    void init_##Name(Type *out, const Type *source) \
    { \
        if (out) *out = *source; \
    }

DEFINE_COPY(unsigned short, word)
DEFINE_COPY(unsigned, dword)
