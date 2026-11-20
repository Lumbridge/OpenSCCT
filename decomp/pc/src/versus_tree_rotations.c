#include "tree_rotation.h"

TREE_LAYOUT(Node8, 8);
TREE_LAYOUT(Node32, 32);

ROTATE(rotate_right_2a0300, Node32, left, right)
ROTATE(rotate_right_2a03a0, Node8, left, right)
ROTATE(rotate_left_2a0710, Node32, right, left)
ROTATE(rotate_left_2a0770, Node8, right, left)
