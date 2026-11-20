struct Node { struct Node *left, *parent, *right; unsigned value, color; };
struct Tree { unsigned reserved; struct Node *head; };
struct Iterator { struct Node *node; };
struct NodeRef { struct Node *ptr; };
struct IteratorRef { struct Iterator *ptr; };
extern struct Node *nil;
extern void release_node(void *);

struct Iterator *__fastcall begin_tree(struct Tree *tree, struct IteratorRef out)
{
    out.ptr->node = tree->head->left;
    return out.ptr;
}

void __fastcall erase_subtree(struct Tree *tree, struct NodeRef root)
{
    struct Node *node = root.ptr;
    for (; node != nil; root.ptr = node) {
        struct NodeRef right;
        right.ptr = node->right;
        erase_subtree(tree, right);
        node = node->left;
        release_node(root.ptr);
    }
}
