struct Tree;
struct Node {
    struct Node *left, *parent, *right;
};
struct Child { struct Node *ptr; };

extern void *__cdecl prepare_48b460(struct Node *, unsigned);
extern void __cdecl release_48b550(void *);

#define ERASE_NODES(Entry, SentinelOffset, Category, Prepare, Release) \
    void __fastcall Entry(struct Tree *tree, struct Child child) \
    { \
        struct Node *node = child.ptr; \
        while (!*((const unsigned char *)child.ptr + SentinelOffset)) { \
            struct Child right; \
            right.ptr = node->right; \
            Entry(tree,right); \
            node = node->left; \
            Release(Prepare(child.ptr, Category)); \
            child.ptr = node; \
        } \
    }

ERASE_NODES(erase_48cee0, 25, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4967c0, 21, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_499800, 21, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_49f430, 21, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4a5910, 21, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4abaf0, 33, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4af480, 21, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4bfe00, 21, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4c5450, 21, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4c95d0, 21, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4c9f40, 21, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4d4400, 49, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4d7460, 21, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4ec5c0, 21, 7, prepare_48b460, release_48b550)
ERASE_NODES(erase_4edc90, 21, 7, prepare_48b460, release_48b550)
