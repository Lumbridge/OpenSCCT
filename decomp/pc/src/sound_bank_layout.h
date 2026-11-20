#ifndef SOUND_BANK_LAYOUT_H
#define SOUND_BANK_LAYOUT_H

typedef struct SoundRecord {
    unsigned long editor_id;
    unsigned char payload[100];
} SoundRecord;

typedef struct RecordTable {
    SoundRecord *entries;
    long count;
} RecordTable;

typedef struct EventGroup {
    unsigned long header;
    RecordTable events;
} EventGroup;

typedef struct LocalBank {
    unsigned char header[12];
    EventGroup *events;
    RecordTable resources;
    unsigned char reserved[28];
    EventGroup *extra_events;
    RecordTable extra_resources;
    unsigned char tail[16];
    int loaded;
} LocalBank;

#endif
