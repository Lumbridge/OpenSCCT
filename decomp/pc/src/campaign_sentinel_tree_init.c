struct Node { struct Node *left, *parent, *right; };
struct Tree { unsigned reserved; struct Node *head; unsigned count; };
struct Argument { unsigned word; };
struct Arguments { unsigned words[2]; };

extern struct Node *__fastcall make_node_387b00(struct Tree *tree);
extern struct Node *__fastcall make_node_4894c0(struct Tree *tree);
extern struct Node *__fastcall make_node_48c630(struct Tree *tree);
extern struct Node *__fastcall make_node_496a50(struct Tree *tree);
extern struct Node *__fastcall make_node_499410(struct Tree *tree);
extern struct Node *__fastcall make_node_49f6d0(struct Tree *tree);
extern struct Node *__fastcall make_node_4a3380(struct Tree *tree);
extern struct Node *__fastcall make_node_4a5c20(struct Tree *tree);
extern struct Node *__fastcall make_node_4abef0(struct Tree *tree);
extern struct Node *__fastcall make_node_4aeec0(struct Tree *tree);
extern struct Node *__fastcall make_node_4aef90(struct Tree *tree);
extern struct Node *__fastcall make_node_4c01d0(struct Tree *tree);
extern struct Node *__fastcall make_node_4c58e0(struct Tree *tree);
extern struct Node *__fastcall make_node_4c96a0(struct Tree *tree);
extern struct Node *__fastcall make_node_4c98a0(struct Tree *tree);
extern struct Node *__fastcall make_node_4cf460(struct Tree *tree);
extern struct Node *__fastcall make_node_4d4030(struct Tree *tree);
extern struct Node *__fastcall make_node_4d7730(struct Tree *tree);
extern struct Node *__fastcall make_node_4de0d0(struct Tree *tree);
extern struct Node *__fastcall make_node_4ec370(struct Tree *tree);
extern struct Node *__fastcall make_node_4eda40(struct Tree *tree);

#define INITIALIZE_BODY(Create, NilOffset) \
    tree->head = Create(tree); \
    ((unsigned char *)tree->head)[NilOffset] = 1; \
    tree->head->parent = tree->head; \
    tree->head->left = tree->head; \
    tree->head->right = tree->head; \
    tree->count = 0; \
    return tree

#define INIT_0(Entry, Create, NilOffset) \
    struct Tree *__fastcall Entry(struct Tree *tree) \
    { INITIALIZE_BODY(Create, NilOffset); }

#define INIT_8(Entry, Create, NilOffset) \
    struct Tree *__fastcall Entry(struct Tree *tree, struct Arguments unused) \
    { INITIALIZE_BODY(Create, NilOffset); }

INIT_8(init_tree_3887c0, make_node_387b00, 17)
INIT_0(init_tree_38adb0, make_node_387b00, 17)
INIT_8(init_tree_48a070, make_node_4894c0, 21)
INIT_0(init_tree_48aba0, make_node_4894c0, 21)
INIT_0(init_tree_48ac60, make_node_4894c0, 21)
INIT_8(init_tree_48d110, make_node_48c630, 25)
INIT_0(init_tree_48e440, make_node_48c630, 25)
INIT_0(init_tree_48e470, make_node_48c630, 25)
INIT_8(init_tree_496d40, make_node_496a50, 21)
INIT_0(init_tree_4975f0, make_node_496a50, 21)
INIT_0(init_tree_497780, make_node_496a50, 21)
INIT_8(init_tree_499880, make_node_499410, 21)
INIT_0(init_tree_49a370, make_node_499410, 21)
INIT_0(init_tree_49a3a0, make_node_499410, 21)
INIT_8(init_tree_49fbf0, make_node_49f6d0, 21)
INIT_0(init_tree_4a0530, make_node_49f6d0, 21)
INIT_0(init_tree_4a0560, make_node_49f6d0, 21)
INIT_8(init_tree_4a3830, make_node_4a3380, 21)
INIT_0(init_tree_4a42e0, make_node_4a3380, 21)
INIT_0(init_tree_4a4310, make_node_4a3380, 21)
INIT_8(init_tree_4a6180, make_node_4a5c20, 21)
INIT_0(init_tree_4a6ff0, make_node_4a5c20, 21)
INIT_0(init_tree_4a7120, make_node_4a5c20, 21)
INIT_8(init_tree_4ac260, make_node_4abef0, 33)
INIT_0(init_tree_4acd70, make_node_4abef0, 33)
INIT_0(init_tree_4ace00, make_node_4abef0, 33)
INIT_0(init_tree_4acf30, make_node_4abef0, 33)
INIT_8(init_tree_4af830, make_node_4aeec0, 33)
INIT_8(init_tree_4af860, make_node_4aef90, 21)
INIT_0(init_tree_4b0ba0, make_node_4aef90, 21)
INIT_0(init_tree_4b0cb0, make_node_4aef90, 21)
INIT_0(init_tree_4b0f40, make_node_4aeec0, 33)
INIT_0(init_tree_4b1120, make_node_4aeec0, 33)
INIT_8(init_tree_4c0500, make_node_4c01d0, 21)
INIT_0(init_tree_4c0db0, make_node_4c01d0, 21)
INIT_0(init_tree_4c0f40, make_node_4c01d0, 21)
INIT_8(init_tree_4c5d40, make_node_4c58e0, 21)
INIT_0(init_tree_4c68b0, make_node_4c58e0, 21)
INIT_0(init_tree_4c68e0, make_node_4c58e0, 21)
INIT_8(init_tree_4ca2c0, make_node_4c96a0, 21)
INIT_8(init_tree_4ca2f0, make_node_4c98a0, 21)
INIT_0(init_tree_4cc4c0, make_node_4c96a0, 21)
INIT_0(init_tree_4cc570, make_node_4c98a0, 21)
INIT_0(init_tree_4cc620, make_node_4c96a0, 21)
INIT_0(init_tree_4cc690, make_node_4c98a0, 21)
INIT_8(init_tree_4cfea0, make_node_4cf460, 149)
INIT_0(init_tree_4d1310, make_node_4cf460, 149)
INIT_0(init_tree_4d1340, make_node_4cf460, 149)
INIT_0(init_tree_4d13b0, make_node_4cf460, 149)
INIT_8(init_tree_4d44e0, make_node_4d4030, 49)
INIT_0(init_tree_4d5230, make_node_4d4030, 49)
INIT_0(init_tree_4d5260, make_node_4d4030, 49)
INIT_0(init_tree_4d5340, make_node_4d4030, 49)
INIT_8(init_tree_4d7e00, make_node_4d7730, 21)
INIT_0(init_tree_4d8850, make_node_4d7730, 21)
INIT_0(init_tree_4d8880, make_node_4d7730, 21)
INIT_8(init_tree_4de510, make_node_4de0d0, 33)
INIT_0(init_tree_4df0f0, make_node_4de0d0, 33)
INIT_0(init_tree_4df2d0, make_node_4de0d0, 33)
INIT_8(init_tree_4ec910, make_node_4ec370, 21)
INIT_0(init_tree_4ed440, make_node_4ec370, 21)
INIT_0(init_tree_4ed4e0, make_node_4ec370, 21)
INIT_8(init_tree_4ee020, make_node_4eda40, 21)
INIT_0(init_tree_4eee50, make_node_4eda40, 21)
INIT_0(init_tree_4eee80, make_node_4eda40, 21)
