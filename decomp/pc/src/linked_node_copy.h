struct Tree;
struct Node;
extern struct Node *__stdcall allocate_node(unsigned bytes);

#define SINGLE_LAYOUT(Tag, K) struct Tag { K key; }
#define PAIR_LAYOUT(Tag, K, V) struct Tag { K key; V value; }
#define COPY_KEY(dst, src) ((dst)->key = (src)->key)
#define COPY_PAIR(dst, src) do { COPY_KEY(dst, src); (dst)->value = (src)->value; } while (0)

#define MAKE_NODE(Name, Tag, Copy) \
    struct Node *__fastcall Name(struct Tree *tree, struct Ref_##Tag source) \
    { \
        struct Node *node = allocate_node(16 + ((sizeof(struct Tag) + 3) & ~3u)); \
        struct Tag *dest = (struct Tag *)((unsigned)node + 16); \
        if (dest) Copy(dest, source.value); \
        return node; \
    }
