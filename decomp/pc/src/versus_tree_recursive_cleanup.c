struct Tree;
struct Node {
    unsigned reserved[2];
    struct Node *left, *right;
    unsigned char value[1];
};
struct Branch { struct Node *node; };
extern void __stdcall release_node(struct Node *node);
#define KEEP_VALUE(node) ((void)0)
#define ERASE_BRANCH(Entry, Destroy) \
    void __fastcall Entry(struct Tree *tree, struct Branch branch) \
    { \
        struct Node *node = branch.node; \
        while (node) { \
            struct Node *left; \
            struct Branch right; \
            right.node = node->right; \
            Entry(tree, right); \
            left = node->left; \
            Destroy(node->value); \
            release_node(node); \
            node = left; \
        } \
    }

extern void __fastcall destroy_value_238bb0(void *value);
extern void __fastcall destroy_value_23f810(void *value);
extern void __fastcall destroy_value_241aa0(void *value);
extern void __fastcall destroy_value_251370(void *value);
extern void __fastcall destroy_value_259470(void *value);
extern void __fastcall destroy_value_25a790(void *value);
extern void __fastcall destroy_value_2be020(void *value);

ERASE_BRANCH(erase_branch_235f60, KEEP_VALUE)
ERASE_BRANCH(erase_branch_23a550, destroy_value_238bb0)
ERASE_BRANCH(erase_branch_23a710, KEEP_VALUE)
ERASE_BRANCH(erase_branch_23a740, KEEP_VALUE)
ERASE_BRANCH(erase_branch_23a770, KEEP_VALUE)
ERASE_BRANCH(erase_branch_23a7a0, destroy_value_2be020)
ERASE_BRANCH(erase_branch_23f7b0, destroy_value_23f810)
ERASE_BRANCH(erase_branch_23f980, KEEP_VALUE)
ERASE_BRANCH(erase_branch_241980, KEEP_VALUE)
ERASE_BRANCH(erase_branch_2419b0, KEEP_VALUE)
ERASE_BRANCH(erase_branch_2419e0, KEEP_VALUE)
ERASE_BRANCH(erase_branch_241a40, destroy_value_241aa0)
ERASE_BRANCH(erase_branch_24b650, destroy_value_2be020)
ERASE_BRANCH(erase_branch_252dc0, KEEP_VALUE)
ERASE_BRANCH(erase_branch_252df0, destroy_value_251370)
ERASE_BRANCH(erase_branch_2596a0, destroy_value_259470)
ERASE_BRANCH(erase_branch_25be40, destroy_value_25a790)
ERASE_BRANCH(erase_branch_264450, KEEP_VALUE)
ERASE_BRANCH(erase_branch_264480, KEEP_VALUE)
ERASE_BRANCH(erase_branch_2644b0, KEEP_VALUE)
ERASE_BRANCH(erase_branch_2644e0, KEEP_VALUE)
ERASE_BRANCH(erase_branch_26b1e0, KEEP_VALUE)
ERASE_BRANCH(erase_branch_2758e0, destroy_value_2be020)
ERASE_BRANCH(erase_branch_275f10, destroy_value_2be020)
