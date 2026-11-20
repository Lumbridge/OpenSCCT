#include "sound_name_tree.h"

extern const char sound_event_name_missing[];
int __cdecl sprintf(char *output, const char *format, ...);
void __stdcall SND_fn_vDisplayErrorEx(int code, const char *context, Word category);

Word __fastcall sound_lookup_event_name(SoundEventNames *tree, NameArg name)
{
    SoundCRC crc;
    SoundNameKey key;
    KeyArg search;
    SoundNameNode *node;
    char message[256];
    Word result;
    sound_construct_crc(&crc);
    key.methods = sound_name_key_methods;
    key.editor_id = (Word)-1;
    key.crc = sound_crc_name(&crc, name);
    search.key = &key;
    node = sound_find_name_node(tree, search);
    if (tree->cursor == tree->sentinel) {
        sprintf(message, sound_event_name_missing, name.text);
        SND_fn_vDisplayErrorEx(8, message, 3);
        sound_destroy_crc(&crc);
        return (Word)-1;
    }
    result = node->key.editor_id;
    sound_destroy_crc(&crc);
    return result;
}
