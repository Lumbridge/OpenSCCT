struct Tree;
struct Node {
    struct Node *left, *parent, *right;
};
struct Child { struct Node *ptr; };

extern void __fastcall destroy_47cf40(void *);
extern void __fastcall destroy_4acf90(void *);
extern void *__cdecl prepare_48b460(struct Node *, unsigned);
extern void __cdecl release_48b550(void *);

#define ERASE_NODES(Entry, SentinelOffset, Category, Prepare, Release, Destroy) \
    void __fastcall Entry(struct Tree *tree, struct Child child) \
    { \
        struct Node *node = child.ptr; \
        while (!*((const unsigned char *)child.ptr + SentinelOffset)) { \
            struct Child right; \
            right.ptr = node->right; \
            Entry(tree,right); \
            node = node->left; \
            Destroy((unsigned char *)child.ptr + 12); \
            Release(Prepare(child.ptr, Category)); \
            child.ptr = node; \
        } \
    }

ERASE_NODES(erase_4a36d0, 21, 7, prepare_48b460, release_48b550, destroy_47cf40)
ERASE_NODES(erase_4cf820, 149, 7, prepare_48b460, release_48b550, destroy_4acf90)
