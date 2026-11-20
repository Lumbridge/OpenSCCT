struct Node { struct Node *next, *previous; };
struct List { struct Node *head; };

extern void __stdcall release_739a40(struct Node *);

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

DESTROY_LIST(destroy_list_5fd860, release_739a40)
DESTROY_LIST(destroy_list_601960, release_739a40)
DESTROY_LIST(destroy_list_60f5f0, release_739a40)
DESTROY_LIST(destroy_list_610150, release_739a40)
DESTROY_LIST(destroy_list_610cc0, release_739a40)
DESTROY_LIST(destroy_list_615f30, release_739a40)
DESTROY_LIST(destroy_list_61e6a0, release_739a40)
DESTROY_LIST(destroy_list_6207f0, release_739a40)
DESTROY_LIST(destroy_list_626ea0, release_739a40)
DESTROY_LIST(destroy_list_62dae0, release_739a40)
DESTROY_LIST(destroy_list_71f8b0, release_739a40)
DESTROY_LIST(destroy_list_722c40, release_739a40)
DESTROY_LIST(destroy_list_722d20, release_739a40)
DESTROY_LIST(destroy_list_722eb0, release_739a40)
DESTROY_LIST(destroy_list_736100, release_739a40)
