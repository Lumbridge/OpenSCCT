struct Node {
    struct Node *left, *parent, *right;
    unsigned value, color;
};
struct Iterator { struct Node *node; };
extern struct Node *nil;
static __forceinline struct Node *subtree_minimum(struct Node *node)
{
    while (node->left != nil) node = node->left;
    return node;
}
static __forceinline struct Node *subtree_maximum(struct Node *node)
{
    while (node->right != nil) node = node->right;
    return node;
}
void __fastcall tree_next(struct Iterator *it)
{
    if (it->node->right != nil)
        it->node = subtree_minimum(it->node->right);
    else {
        struct Node *parent;
        while (it->node == (parent = it->node->parent)->right)
            it->node = parent;
        if (it->node->right != parent)
            it->node = parent;
    }
}
void __fastcall tree_previous(struct Iterator *it)
{
    if (it->node->color == 0 && it->node->parent->parent == it->node)
        it->node = it->node->right;
    else if (it->node->left != nil)
        it->node = subtree_maximum(it->node->left);
    else {
        struct Node *parent;
        while (it->node == (parent = it->node->parent)->left)
            it->node = parent;
        it->node = parent;
    }
}
