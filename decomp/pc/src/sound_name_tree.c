#include "sound_name_tree.h"

SoundNameNode *__fastcall sound_find_name_node(SoundEventNames *tree, KeyArg argument)
{
    const SoundNameKey *key = argument.key;
    SoundNameNode *sentinel = tree->sentinel;
    sentinel->key.editor_id = key->editor_id;
    sentinel->key.crc = key->crc;
    tree->cursor = tree->root;
    while (tree->cursor->key.crc != key->crc) {
        tree->cursor = key->crc < tree->cursor->key.crc ? tree->cursor->lower : tree->cursor->upper;
    }
    return tree->cursor;
}

SoundNameNode *__cdecl sound_rotate_name_right(SoundNameNode *node)
{
    SoundNameNode *child = node->lower;
    node->lower = child->upper;
    child->upper = node;
    return child;
}

SoundNameNode *__cdecl sound_rotate_name_left(SoundNameNode *node)
{
    SoundNameNode *child = node->upper;
    node->upper = child->lower;
    child->lower = node;
    return child;
}

SoundNameNode *__fastcall sound_init_name_node(SoundNameNode *node)
{
    node->key.methods = sound_name_key_methods;
    node->key.editor_id = (Word)-1;
    node->key.crc = (Word)-1;
    node->lower = 0;
    node->upper = 0;
    node->field_14 = 1;
    node->level = 1;
    return node;
}

void __stdcall sound_skew_name_node(SoundNameNode **link)
{
    if ((*link)->lower->level == (*link)->level)
        *link = sound_rotate_name_right(*link);
}

void __stdcall sound_split_name_node(SoundNameNode **link)
{
    if ((*link)->upper->upper->level == (*link)->level) {
        *link = sound_rotate_name_left(*link);
        ++(*link)->level;
    }
}

void *__stdcall SND_fn_pvMallocSnd(Word size);

SoundEventNames *__fastcall sound_construct_name_tree(SoundEventNames *tree)
{
    SoundNameNode *node = SND_fn_pvMallocSnd(sizeof(*node));
    tree->sentinel = node ? sound_init_name_node(node) : 0;
    tree->sentinel->lower = tree->sentinel->upper = tree->sentinel;
    tree->sentinel->level = 0;
    tree->root = tree->sentinel;
    return tree;
}
