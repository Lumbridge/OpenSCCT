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
    tree->count = 0

#define INIT_0(Entry, Create, NilOffset) \
    void __fastcall Entry(struct Tree *tree) \
    { INITIALIZE_BODY(Create, NilOffset); }

INIT_0(init_tree_388260, make_node_387b00, 17)
INIT_0(init_tree_4897d0, make_node_4894c0, 21)
INIT_0(init_tree_48c9b0, make_node_48c630, 25)
INIT_0(init_tree_496c50, make_node_496a50, 21)
INIT_0(init_tree_4995f0, make_node_499410, 21)
INIT_0(init_tree_49faf0, make_node_49f6d0, 21)
INIT_0(init_tree_4a3560, make_node_4a3380, 21)
INIT_0(init_tree_4a5e70, make_node_4a5c20, 21)
INIT_0(init_tree_4ac110, make_node_4abef0, 33)
INIT_0(init_tree_4af2f0, make_node_4aeec0, 33)
INIT_0(init_tree_4af320, make_node_4aef90, 21)
INIT_0(init_tree_4c0410, make_node_4c01d0, 21)
INIT_0(init_tree_4c5b80, make_node_4c58e0, 21)
INIT_0(init_tree_4c9be0, make_node_4c96a0, 21)
INIT_0(init_tree_4c9c50, make_node_4c98a0, 21)
INIT_0(init_tree_4cf700, make_node_4cf460, 149)
INIT_0(init_tree_4d4220, make_node_4d4030, 49)
INIT_0(init_tree_4d7940, make_node_4d7730, 21)
INIT_0(init_tree_4de2b0, make_node_4de0d0, 33)
INIT_0(init_tree_4ec530, make_node_4ec370, 21)
INIT_0(init_tree_4edba0, make_node_4eda40, 21)
