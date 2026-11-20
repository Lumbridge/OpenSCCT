#include "sound_bank_layout.h"

#define BANK_A ((LocalBank *)0x1132b880)
#define BANK_B ((LocalBank *)0x1132b8f8)

static SoundRecord *find_record(RecordTable *table, unsigned long id)
{
    long index;
    SoundRecord *record = table->entries;
    for (index = 0; index < table->count; ++index, ++record)
        if (record->editor_id == id)
            return record;
    return 0;
}

SoundRecord *__stdcall sound_resource_by_editor_id_mode1(unsigned long id)
{
    SoundRecord *record;
    if (BANK_B->loaded) {
        if ((record = find_record(&BANK_B->resources, id)) != 0)
            return record;
        if ((record = find_record(&BANK_B->extra_resources, id)) != 0)
            return record;
    }
    if (BANK_A->loaded) {
        if ((record = find_record(&BANK_A->resources, id)) != 0)
            return record;
        if ((record = find_record(&BANK_A->extra_resources, id)) != 0)
            return record;
    }
    return 0;
}
