struct WordList { int *values; int count, capacity; };
struct WordRef { const int *ptr; };
struct IndexRef { int *ptr; };

#define FIND_WORD(Entry) \
int __fastcall Entry(struct WordList *list, struct WordRef word, struct IndexRef index) \
{ \
    for (*index.ptr = 0; *index.ptr < list->count; ++*index.ptr) { \
        if (list->values[*index.ptr] == *word.ptr) \
            return 1; \
    } \
    return 0; \
}

FIND_WORD(find_word_05e4c0)
FIND_WORD(find_word_090be0)
FIND_WORD(find_word_0d7480)
FIND_WORD(find_word_136d80)
FIND_WORD(find_word_1dfb30)
