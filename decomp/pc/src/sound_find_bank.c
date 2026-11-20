#include "sound_bank_layout.h"

typedef struct GroupCounts {
    unsigned long unknown;
    long event_count, resource_count;
} GroupCounts;
typedef struct SoundBank {
    unsigned long active;
    unsigned char unknown[44];
    const GroupCounts *counts;
    SoundRecord *events, *resources;
} SoundBank;
typedef struct BankLink {
    struct BankLink *next;
    unsigned long unknown;
    SoundBank *bank;
} BankLink;

#define BANKS (*(BankLink **)0x1132e93c)

SoundRecord *__stdcall sound_resource_by_editor_id_mode0(unsigned long id)
{
    BankLink *link = BANKS;
    while (link) {
        BankLink *current = link;
        SoundBank *bank;
        link = link->next;
        bank = current->bank;
        if (bank->active) {
            long i;
            for (i = 0; i < bank->counts->resource_count; ++i) {
                if (bank->resources[i].editor_id == id)
                    return &bank->resources[i];
            }
        }
    }
    return 0;
}
