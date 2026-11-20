#include "sound_bank_layout.h"

extern LocalBank bank_a, bank_b;
void __stdcall SND_fn_vDisplayError(int code, const char *context);

SoundRecord *__stdcall sound_bin_res_mode1(long id)
{
    LocalBank *bank;
    RecordTable *table;
    unsigned long extra;
    SoundRecord *record;
    if (id == -2)
        return 0;
    bank = ((unsigned long)id >> 31) ? &bank_b : &bank_a;
    extra = ((unsigned long)id >> 30) & 1;
    id &= 0xffff;
    table = extra ? &bank->extra_resources : &bank->resources;
    record = table->entries + id;
    if (!record)
        SND_fn_vDisplayError(8, (const char *)0x111720dc);
    return record;
}

SoundRecord *__cdecl sound_bin_event_mode1(long id)
{
    LocalBank *bank;
    RecordTable *table;
    unsigned long extra;
    SoundRecord *record;
    bank = ((unsigned long)id >> 31) ? &bank_b : &bank_a;
    extra = ((unsigned long)id >> 30) & 1;
    id &= 0xffff;
    table = extra ? &bank->extra_events->events : &bank->events->events;
    record = table->entries + id;
    if (!record)
        SND_fn_vDisplayError(8, (const char *)0x11172108);
    return record;
}
