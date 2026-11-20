#include "linked_tree_insert.h"

extern struct Node *__fastcall make_node_5ee0c0(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_5ee0f0(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_5ee120(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_5ee150(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_619050(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_619640(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_6213c0(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_625b90(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_628990(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_62d600(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_62d630(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_62f3d0(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_727f10(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_735eb0(struct Tree *, struct Value);

INSERT_NODE(insert_node_5ed8a0, make_node_5ee0c0, u32)
INSERT_NODE(insert_node_5eda70, make_node_5ee0f0, u32)
INSERT_NODE(insert_node_5edc40, make_node_5ee120, u32)
INSERT_NODE(insert_node_5ede10, make_node_5ee150, u32)
INSERT_NODE(insert_node_619080, make_node_619050, u16)
INSERT_NODE(insert_node_619250, make_node_619640, u16)
INSERT_NODE(insert_node_620ff0, make_node_6213c0, s32)
INSERT_NODE(insert_node_626a00, make_node_625b90, u32)
INSERT_NODE(insert_node_6287c0, make_node_628990, u32)
INSERT_NODE(insert_node_62ce20, make_node_62d600, u32)
INSERT_NODE(insert_node_62cff0, make_node_62d630, u32)
INSERT_NODE(insert_node_62f080, make_node_62f3d0, u8)
INSERT_NODE(insert_node_727d10, make_node_727f10, s32)
INSERT_NODE(insert_node_735c40, make_node_735eb0, u16)

extern int __cdecl compare_ranges(const unsigned char *, const unsigned char *,
                                  const unsigned char *, const unsigned char *);
#define RANGE_LESS (compare_ranges(value.data->range.first, value.data->range.end, \
                                   parent->key.range.first, parent->key.range.end) < 0)
extern struct Node *__fastcall make_node_5fa9b0(struct Tree *, struct Value);
INSERT_WITH(insert_node_5fa0d0, make_node_5fa9b0, RANGE_LESS, rotate_right_call)
extern struct Node *__fastcall make_node_5faa70(struct Tree *, struct Value);
INSERT_WITH(insert_node_5fa440, make_node_5faa70, RANGE_LESS, rotate_right_call)
