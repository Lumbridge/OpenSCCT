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

extern void __fastcall destroy_value_5f5ad0(void *value);
extern void __fastcall destroy_value_5f6fb0(void *value);
extern void __fastcall destroy_value_5f77c0(void *value);
extern void __fastcall destroy_value_5f77d0(void *value);
extern void __fastcall destroy_value_601e90(void *value);
extern void __fastcall destroy_value_604640(void *value);
extern void __fastcall destroy_value_616880(void *value);
extern void __fastcall destroy_value_6205a0(void *value);
extern void __fastcall destroy_value_621c90(void *value);
extern void __fastcall destroy_value_62a7a0(void *value);
extern void __fastcall destroy_value_62b080(void *value);

ERASE_BRANCH(erase_branch_5ed3e0, KEEP_VALUE)
ERASE_BRANCH(erase_branch_5ed410, KEEP_VALUE)
ERASE_BRANCH(erase_branch_5ed440, KEEP_VALUE)
ERASE_BRANCH(erase_branch_5ed470, KEEP_VALUE)
ERASE_BRANCH(erase_branch_5f8780, destroy_value_5f77c0)
ERASE_BRANCH(erase_branch_5f8dc0, destroy_value_5f77d0)
ERASE_BRANCH(erase_branch_5f9400, destroy_value_5f5ad0)
ERASE_BRANCH(erase_branch_5f9820, KEEP_VALUE)
ERASE_BRANCH(erase_branch_601e30, destroy_value_601e90)
ERASE_BRANCH(erase_branch_602020, KEEP_VALUE)
ERASE_BRANCH(erase_branch_604520, KEEP_VALUE)
ERASE_BRANCH(erase_branch_604550, KEEP_VALUE)
ERASE_BRANCH(erase_branch_604580, KEEP_VALUE)
ERASE_BRANCH(erase_branch_6045e0, destroy_value_604640)
ERASE_BRANCH(erase_branch_60f680, destroy_value_5f6fb0)
ERASE_BRANCH(erase_branch_618a20, KEEP_VALUE)
ERASE_BRANCH(erase_branch_618a80, destroy_value_616880)
ERASE_BRANCH(erase_branch_620a40, destroy_value_6205a0)
ERASE_BRANCH(erase_branch_623800, destroy_value_621c90)
ERASE_BRANCH(erase_branch_628790, KEEP_VALUE)
ERASE_BRANCH(erase_branch_62c4c0, destroy_value_62b080)
ERASE_BRANCH(erase_branch_62c680, KEEP_VALUE)
ERASE_BRANCH(erase_branch_62c6b0, KEEP_VALUE)
ERASE_BRANCH(erase_branch_62c6e0, KEEP_VALUE)
ERASE_BRANCH(erase_branch_62c710, destroy_value_62a7a0)
ERASE_BRANCH(erase_branch_62eb50, KEEP_VALUE)
ERASE_BRANCH(erase_branch_723200, KEEP_VALUE)
ERASE_BRANCH(erase_branch_723230, KEEP_VALUE)
ERASE_BRANCH(erase_branch_727b90, KEEP_VALUE)
ERASE_BRANCH(erase_branch_728c70, KEEP_VALUE)
