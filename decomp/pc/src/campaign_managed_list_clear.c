struct Node { struct Node *next, *prev; };
struct List { unsigned reserved; struct Node *head; unsigned count; };
extern void *__cdecl get_manager(void);
extern void __cdecl release_allocation(void *, void *, unsigned);

#define CLEAR_LIST(Entry, Category) \
    void __fastcall Entry(struct List *list) \
    { \
        struct Node *node = list->head->next; \
        list->head->next = list->head; \
        list->head->prev = list->head; \
        list->count = 0; \
        while (node != list->head) { \
            struct Node *next = node->next; \
            release_allocation(get_manager(), node, Category); \
            node = next; \
        } \
    }

CLEAR_LIST(clear_list_1bca40, 7)
CLEAR_LIST(clear_list_481300, 7)
CLEAR_LIST(clear_list_48c120, 7)
CLEAR_LIST(clear_list_48c520, 7)
CLEAR_LIST(clear_list_494500, 7)
CLEAR_LIST(clear_list_494c50, 7)
CLEAR_LIST(clear_list_49a6e0, 7)
CLEAR_LIST(clear_list_4a14f0, 7)
CLEAR_LIST(clear_list_4a15d0, 7)
CLEAR_LIST(clear_list_4a7f90, 7)
CLEAR_LIST(clear_list_4b1950, 7)
CLEAR_LIST(clear_list_4b68f0, 7)
CLEAR_LIST(clear_list_4b92d0, 7)
CLEAR_LIST(clear_list_4b9760, 7)
CLEAR_LIST(clear_list_4ba430, 7)
CLEAR_LIST(clear_list_4bdfb0, 7)
CLEAR_LIST(clear_list_4c28b0, 7)
CLEAR_LIST(clear_list_4c57c0, 7)
CLEAR_LIST(clear_list_4c6c60, 7)
CLEAR_LIST(clear_list_4cd100, 7)
CLEAR_LIST(clear_list_4cf350, 7)
CLEAR_LIST(clear_list_4d5e50, 7)
CLEAR_LIST(clear_list_4dd0d0, 7)
CLEAR_LIST(clear_list_4de400, 7)
CLEAR_LIST(clear_list_4e2f60, 7)
CLEAR_LIST(clear_list_4e3040, 7)
CLEAR_LIST(clear_list_4e4a30, 7)
CLEAR_LIST(clear_list_4e6ae0, 7)
CLEAR_LIST(clear_list_4f7900, 7)
CLEAR_LIST(clear_list_73ac60, 7)
