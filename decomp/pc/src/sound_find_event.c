#include "sound_bank_layout.h"

extern EventGroup *bank_a_events;
extern EventGroup *bank_b_events;
extern EventGroup *bank_a_extra_events;
extern EventGroup *bank_b_extra_events;

#define USE_EXTRA_EVENTS (*(const long *)0x1132ba68)

static __forceinline SoundRecord *find_record(RecordTable *table, unsigned long id)
{
    long index;
    if (table) {
        SoundRecord *record = table->entries;
        for (index = 0; index < table->count; ++index, ++record)
            if (record->editor_id == id)
                return record;
    }
    return 0;
}

SoundRecord *__cdecl sound_handle_by_editor_id_mode1(unsigned long id)
{
    SoundRecord *event;
    if (bank_a_events && (event = find_record(&bank_a_events->events, id)))
        return event;
    if (bank_b_events && (event = find_record(&bank_b_events->events, id)))
        return event;
    if (USE_EXTRA_EVENTS) {
        if (bank_a_extra_events && (event = find_record(&bank_a_extra_events->events, id)))
            return event;
        if (bank_b_extra_events && (event = find_record(&bank_b_extra_events->events, id)))
            return event;
    }
    return 0;
}
