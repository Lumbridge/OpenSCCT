#include "tree_iteration.h"

TREE_LAYOUT(Node32, 32);
TREE_LAYOUT(Node40, 40);

ITERATE(tree_prev_2a51f0, Node32, left, right, 1)
ITERATE(tree_next_2a0400, Node32, right, left, 0)
ITERATE(tree_next_2b0e30, Node40, right, left, 0)
