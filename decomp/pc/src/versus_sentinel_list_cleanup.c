struct Node { struct Node *next, *previous; };
struct List { struct Node *head; };

extern void __stdcall release_171510(struct Node *);

#define DESTROY_LIST(Entry, Release) \
    void __fastcall Entry(struct List *list) \
    { \
        struct Node *node = list->head->next; \
        while (node != list->head) { \
            struct Node *removed = node; \
            node = node->next; \
            Release(removed); \
        } \
        list->head->next = list->head; \
        list->head->previous = list->head; \
        Release(list->head); \
    }

DESTROY_LIST(destroy_list_23c1d0, release_171510)
