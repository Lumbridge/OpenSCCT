struct Tree;
struct Node {
    struct Node *left, *parent, *right;
    unsigned char fields[5];
    unsigned char sentinel;
};
struct Child { struct Node *ptr; };
struct Heap;
struct Methods {
    void *reserved[2];
    void (__fastcall *release)(struct Heap *, const struct Methods *, struct Node *);
};
struct Heap { const struct Methods *methods; };
extern struct Heap *heap;
void __fastcall erase_nodes(struct Tree *tree, struct Child child)
{
    struct Node *node = child.ptr;
    while (!child.ptr->sentinel) {
        struct Child right;
        right.ptr = node->right;
        erase_nodes(tree, right);
        node = node->left;
        heap->methods->release(heap, heap->methods, child.ptr);
        child.ptr = node;
    }
}
