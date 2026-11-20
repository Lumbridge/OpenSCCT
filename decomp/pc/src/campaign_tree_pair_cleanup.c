struct Tree;
struct Pair { void *first, *second; };
struct Node {
    struct Node *left, *parent, *right;
    struct Pair value;
    unsigned char colour;
    unsigned char sentinel;
};
struct Child { struct Node *ptr; };
extern void __fastcall destroy_field(void *);
extern void *__cdecl prepare_release(struct Node *, unsigned);
extern void __cdecl release_prepared(void *);
void __fastcall erase_nodes(struct Tree *tree, struct Child child)
{
    struct Node *node = child.ptr;
    while (!child.ptr->sentinel) {
        struct Child right;
        right.ptr = node->right;
        erase_nodes(tree, right);
        node = node->left;
        {
            struct Pair *pair = &child.ptr->value;
            destroy_field(&pair->second);
            destroy_field(pair);
        }
        release_prepared(prepare_release(child.ptr, 7));
        child.ptr = node;
    }
}
