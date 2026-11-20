#include "sound_name_tree.h"

static __inline Word sound_reflect_bits(Word bits, signed char width)
{
    Word result = 0;
    int i;
    for (i = width - 1; i > -1; --i) {
        if (bits & 1)
            result |= 1ul << i;
        bits >>= 1;
    }
    return result;
}

void __fastcall sound_init_crc_table(SoundCRC *crc)
{
    int i, j;
    for (i = 0; i <= 255; ++i) {
        crc->table[i] = sound_reflect_bits(i, 8) << 24;
        for (j = 0; j < 8; ++j)
            crc->table[i] = (crc->table[i] << 1) ^ ((crc->table[i] & 0x80000000ul) ? 0x04c11db7ul : 0);
        crc->table[i] = sound_reflect_bits(crc->table[i], 32);
    }
}

Word __fastcall sound_crc_name(const SoundCRC *crc, NameArg name)
{
    Word result = 0xfffffffful;
    const unsigned char *text = (const unsigned char *)name.text;
    while (*text) {
        result = (result >> 8) ^ crc->table[(result & 0xff) ^ *text];
        ++text;
    }
    return ~result;
}

SoundCRC *__fastcall sound_construct_crc(SoundCRC *crc)
{
    crc->methods = sound_crc_methods;
    sound_init_crc_table(crc);
    return crc;
}

void __fastcall sound_destroy_crc(SoundCRC *crc)
{
    crc->methods = sound_crc_methods;
}
