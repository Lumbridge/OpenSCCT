struct Node { unsigned char color; struct Node *parent, *left, *right; };
#define ROTATE(Name, Near, Far) \
    static __forceinline void Name(struct Node *node, struct Node **root) \
    { \
        struct Node *pivot = node->Near; \
        node->Near = pivot->Far; \
        if (pivot->Far) pivot->Far->parent = node; \
        pivot->parent = node->parent; \
        if (node == *root) *root = pivot; \
        else if (node == node->parent->Far) node->parent->Far = pivot; \
        else node->parent->Near = pivot; \
        pivot->Far = node; \
        node->parent = pivot; \
    }
ROTATE(rotate_left, right, left)
ROTATE(rotate_right, left, right)

#define FIX_SIDE(Near, Far, Rotate, Opposite) \
            struct Node *sibling = parent->Far; \
            if (!sibling->color) { \
                sibling->color = 1; parent->color = 0; \
                Rotate(parent, root); sibling = parent->Far; \
            } \
            if ((!sibling->Near || sibling->Near->color == 1) && (!sibling->Far || sibling->Far->color == 1)) { \
                sibling->color = 0; child = parent; parent = parent->parent; \
            } else { \
                if (!sibling->Far || sibling->Far->color == 1) { \
                    if (sibling->Near) sibling->Near->color = 1; \
                    sibling->color = 0; Opposite(sibling, root); sibling = parent->Far; \
                } \
                sibling->color = parent->color; parent->color = 1; \
                if (sibling->Far) sibling->Far->color = 1; \
                Rotate(parent, root); break; \
            }

struct Node *unlink_node(struct Node *node, struct Node **root,
                         struct Node **leftmost, struct Node **rightmost)
{
    struct Node *removed = node, *child, *parent;
    if (!removed->left) child = removed->right;
    else if (!removed->right) child = removed->left;
    else {
        removed = removed->right;
        while (removed->left) removed = removed->left;
        child = removed->right;
    }
    if (removed != node) {
        node->left->parent = removed;
        removed->left = node->left;
        if (removed != node->right) {
            parent = removed->parent;
            if (child) child->parent = removed->parent;
            removed->parent->left = child;
            removed->right = node->right;
            node->right->parent = removed;
        } else parent = removed;
        if (*root == node) *root = removed;
        else if (node->parent->left == node) node->parent->left = removed;
        else node->parent->right = removed;
        removed->parent = node->parent;
        { unsigned char tmp = removed->color; removed->color = node->color; node->color = tmp; }
        removed = node;
    } else {
        parent = removed->parent;
        if (child) child->parent = removed->parent;
        if (*root == node) *root = child;
        else if (node->parent->left == node) node->parent->left = child;
        else node->parent->right = child;
        if (*leftmost == node) {
            if (!node->right) *leftmost = node->parent;
            else {
                struct Node *cursor = child;
                while (cursor->left) cursor = cursor->left;
                *leftmost = cursor;
            }
        }
        if (*rightmost == node) {
            if (!node->left) *rightmost = node->parent;
            else {
                struct Node *cursor = child;
                while (cursor->right) cursor = cursor->right;
                *rightmost = cursor;
            }
        }
    }
    if (removed->color != 0) {
        while (child != *root && (!child || child->color == 1)) {
            if (child == parent->left) {
                FIX_SIDE(left, right, rotate_left, rotate_right)
            } else {
                FIX_SIDE(right, left, rotate_right, rotate_left)
            }
        }
        if (child) child->color = 1;
    }
    return removed;
}
