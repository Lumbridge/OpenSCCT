struct Node { unsigned char color; struct Node *parent, *left, *right; };

struct Node *next_node_5ed860(struct Node *node)
{
    struct Node *parent;
    if (node->right) {
        node = node->right;
        while (node->left) node = node->left;
        return node;
    }
    parent = node->parent;
    while (node == parent->right) {
        node = parent;
        parent = parent->parent;
    }
    return node->right != parent ? parent : node;
}

struct Node *prev_node_5edfe0(struct Node *node)
{
    struct Node *parent;
    if (!node->color && node->parent->parent == node) return node->right;
    if (node->left) {
        node = node->left;
        while (node->right) node = node->right;
        return node;
    }
    parent = node->parent;
    while (node == parent->left) {
        node = parent;
        parent = parent->parent;
    }
    return parent;
}

#define ROTATE(Name, Near, Far) \
    void Name(struct Node *node, struct Node **root) \
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
ROTATE(rotate_left_5ee020, right, left)
ROTATE(rotate_right_5ee070, left, right)
