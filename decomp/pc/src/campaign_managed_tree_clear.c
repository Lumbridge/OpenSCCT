struct Node;
struct NodeArg { struct Node *node; };
struct Node { struct Node *left; struct NodeArg parent; struct Node *right; };
struct Tree { unsigned reserved; struct Node *head; unsigned count; };
#define CLEAR_TREE(Entry, Erase) \
    void __fastcall Entry(struct Tree *tree) \
    { \
        Erase(tree, tree->head->parent); \
        tree->head->parent.node = tree->head; \
        tree->count = 0; \
        tree->head->left = tree->head; \
        tree->head->right = tree->head; \
    }

extern void __fastcall erase_385890(struct Tree *, struct NodeArg);
extern void __fastcall erase_422950(struct Tree *, struct NodeArg);
extern void __fastcall erase_453f40(struct Tree *, struct NodeArg);
extern void __fastcall erase_489ce0(struct Tree *, struct NodeArg);
extern void __fastcall erase_48cee0(struct Tree *, struct NodeArg);
extern void __fastcall erase_4967c0(struct Tree *, struct NodeArg);
extern void __fastcall erase_499800(struct Tree *, struct NodeArg);
extern void __fastcall erase_49f430(struct Tree *, struct NodeArg);
extern void __fastcall erase_4a36d0(struct Tree *, struct NodeArg);
extern void __fastcall erase_4a5910(struct Tree *, struct NodeArg);
extern void __fastcall erase_4abaf0(struct Tree *, struct NodeArg);
extern void __fastcall erase_4af480(struct Tree *, struct NodeArg);
extern void __fastcall erase_4b06d0(struct Tree *, struct NodeArg);
extern void __fastcall erase_4bfe00(struct Tree *, struct NodeArg);
extern void __fastcall erase_4c5450(struct Tree *, struct NodeArg);
extern void __fastcall erase_4c95d0(struct Tree *, struct NodeArg);
extern void __fastcall erase_4c9f40(struct Tree *, struct NodeArg);
extern void __fastcall erase_4cf820(struct Tree *, struct NodeArg);
extern void __fastcall erase_4d4400(struct Tree *, struct NodeArg);
extern void __fastcall erase_4d7460(struct Tree *, struct NodeArg);
extern void __fastcall erase_4de850(struct Tree *, struct NodeArg);
extern void __fastcall erase_4ec5c0(struct Tree *, struct NodeArg);
extern void __fastcall erase_4edc90(struct Tree *, struct NodeArg);

CLEAR_TREE(clear_387ad0, erase_385890)
CLEAR_TREE(clear_422af0, erase_422950)
CLEAR_TREE(clear_453fa0, erase_453f40)
CLEAR_TREE(clear_48a040, erase_489ce0)
CLEAR_TREE(clear_48d0c0, erase_48cee0)
CLEAR_TREE(clear_496a20, erase_4967c0)
CLEAR_TREE(clear_499850, erase_499800)
CLEAR_TREE(clear_49f6a0, erase_49f430)
CLEAR_TREE(clear_4a37e0, erase_4a36d0)
CLEAR_TREE(clear_4a5bc0, erase_4a5910)
CLEAR_TREE(clear_4abd90, erase_4abaf0)
CLEAR_TREE(clear_4af630, erase_4af480)
CLEAR_TREE(clear_4b0840, erase_4b06d0)
CLEAR_TREE(clear_4c01a0, erase_4bfe00)
CLEAR_TREE(clear_4c58b0, erase_4c5450)
CLEAR_TREE(clear_4c9b70, erase_4c95d0)
CLEAR_TREE(clear_4ca320, erase_4c9f40)
CLEAR_TREE(clear_4cfac0, erase_4cf820)
CLEAR_TREE(clear_4d44b0, erase_4d4400)
CLEAR_TREE(clear_4d7700, erase_4d7460)
CLEAR_TREE(clear_4decf0, erase_4de850)
CLEAR_TREE(clear_4ec780, erase_4ec5c0)
CLEAR_TREE(clear_4edd70, erase_4edc90)
