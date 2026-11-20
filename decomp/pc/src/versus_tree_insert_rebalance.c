#include "linked_tree_insert.h"

extern struct Node *__fastcall make_node_23ba70(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_253390(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_253920(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_259ff0(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_2646e0(struct Tree *, struct Value);
extern struct Node *__fastcall make_node_26ba50(struct Tree *, struct Value);

INSERT_NODE(insert_node_23b310, make_node_23ba70, u32)
INSERT_NODE(insert_node_2533c0, make_node_253390, u16)
INSERT_NODE(insert_node_253590, make_node_253920, u16)
INSERT_NODE(insert_node_259c40, make_node_259ff0, s32)
INSERT_NODE(insert_node_264510, make_node_2646e0, u32)
INSERT_NODE(insert_node_26b700, make_node_26ba50, u8)

extern int __cdecl compare_ranges(const unsigned char *, const unsigned char *,
                                  const unsigned char *, const unsigned char *);
#define RANGE_LESS (compare_ranges(value.data->range.first, value.data->range.end, \
                                   parent->key.range.first, parent->key.range.end) < 0)
extern struct Node *__fastcall make_node_2763d0(struct Tree *, struct Value);
INSERT_WITH(insert_node_275f70, make_node_2763d0, RANGE_LESS, rotate_right_call)
