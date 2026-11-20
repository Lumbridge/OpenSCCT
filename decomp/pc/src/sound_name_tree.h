#ifndef SOUND_NAME_TREE_H
#define SOUND_NAME_TREE_H

typedef unsigned long Word;

typedef struct SoundCRC {
    const void *methods;
    Word table[256];
} SoundCRC;

typedef struct NameArg { const char *text; } NameArg;
typedef struct SoundNameKey {
    const void *methods;
    Word editor_id;
    Word crc;
} SoundNameKey;
typedef struct SoundNameNode {
    SoundNameKey key;
    struct SoundNameNode *lower, *upper;
    int field_14, level;
} SoundNameNode;
typedef struct SoundEventNames {
    SoundNameNode *root, *sentinel, *cursor;
} SoundEventNames;
typedef struct KeyArg { const SoundNameKey *key; } KeyArg;

extern const char sound_crc_methods[];
extern const char sound_name_key_methods[];
SoundCRC *__fastcall sound_construct_crc(SoundCRC *crc);
void __fastcall sound_destroy_crc(SoundCRC *crc);
Word __fastcall sound_crc_name(const SoundCRC *crc, NameArg name);
SoundNameNode *__fastcall sound_find_name_node(SoundEventNames *tree, KeyArg argument);

#endif
