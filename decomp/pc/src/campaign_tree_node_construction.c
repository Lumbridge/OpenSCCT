struct Value1 { unsigned words[1]; };
struct Node1 { struct Node1 *left, *parent, *right; struct Value1 value; unsigned char color, sentinel; };
struct Arg1 { struct Node1 *left, *parent, *right; const struct Value1 *value; unsigned char color; };
struct Value2 { unsigned words[2]; };
struct Node2 { struct Node2 *left, *parent, *right; struct Value2 value; unsigned char color, sentinel; };
struct Arg2 { struct Node2 *left, *parent, *right; const struct Value2 *value; unsigned char color; };
struct Value3 { unsigned words[3]; };
struct Node3 { struct Node3 *left, *parent, *right; struct Value3 value; unsigned char color, sentinel; };
struct Arg3 { struct Node3 *left, *parent, *right; const struct Value3 *value; unsigned char color; };

#define INIT_NODE(Entry, N, Args) \
    struct N *__fastcall Entry(struct N *node, struct Args args) \
    { \
        node->left = args.left; \
        node->parent = args.parent; \
        node->right = args.right; \
        node->value = *args.value; \
        node->color = args.color; \
        node->sentinel = 0; \
        return node; \
    }

INIT_NODE(init_tree_node_380690, Node1, Arg1)
INIT_NODE(init_tree_node_48be40, Node3, Arg3)
INIT_NODE(init_tree_node_4968c0, Node2, Arg2)
INIT_NODE(init_tree_node_4994d0, Node2, Arg2)
INIT_NODE(init_tree_node_49f530, Node2, Arg2)
INIT_NODE(init_tree_node_4a5a10, Node2, Arg2)
INIT_NODE(init_tree_node_4af0a0, Node2, Arg2)
INIT_NODE(init_tree_node_4bff00, Node2, Arg2)
INIT_NODE(init_tree_node_4c5560, Node2, Arg2)
INIT_NODE(init_tree_node_4c93a0, Node2, Arg2)
INIT_NODE(init_tree_node_4c93e0, Node2, Arg2)
INIT_NODE(init_tree_node_4d7560, Node2, Arg2)
