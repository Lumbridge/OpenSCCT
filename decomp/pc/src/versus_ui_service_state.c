struct StateRecord;
struct RecordArgument { struct StateRecord *record; };
typedef void (__fastcall *RecordMethod)(struct StateRecord *, struct RecordArgument);
struct StateRecord { RecordMethod *methods; unsigned fields[3]; };
struct Service {
    unsigned char depth, padding[3];
    struct StateRecord records[10];
    unsigned char opaque[176];
    unsigned char channel, reserved[3];
    int current[10], previous[10];
    float value, saved, adjustment;
    float channel_values[10];
    unsigned char changed;
};

void __fastcall push_record(struct Service *service, unsigned unused, const struct StateRecord *source)
{
    struct StateRecord *record = &service->records[service->depth];
    record->fields[0] = source->fields[0];
    record->fields[1] = source->fields[1];
    record->fields[2] = source->fields[2];
    ++service->depth;
}

void __fastcall pop_record(struct Service *service)
{
    --service->depth;
}

void __fastcall prepare_record(struct Service *service, unsigned unused,
                               struct StateRecord *record, struct RecordArgument input)
{
    record->methods[3](record, input);
    if (service->depth) {
        struct RecordArgument top;
        top.record = &service->records[service->depth - 1];
        if (top.record)
            record->methods[4](record, top);
    }
}

void __fastcall set_channel_state(struct Service *service, unsigned unused, int state)
{
    int old = service->current[service->channel];
    if (state != old && (state != 2 || old != 0)) {
        service->previous[service->channel] = old;
        service->current[service->channel] = state;
        if (state == 1) {
            service->saved = service->value;
            service->value -= service->adjustment;
        }
        service->changed = 1;
    }
}

extern const float channel_scale;

void __fastcall push_channel(struct Service *service)
{
    service->adjustment *= channel_scale;
    switch (service->current[service->channel]) {
    case 2:
        service->channel_values[service->channel] = service->value;
        service->value = service->saved;
        break;
    case 0:
        service->channel_values[service->channel] = service->value;
        break;
    }
    service->current[service->channel + 1] = service->current[service->channel];
    service->previous[service->channel + 1] = service->previous[service->channel];
    ++service->channel;
    service->previous[service->channel] = 0;
    service->changed = 1;
}

void __fastcall pop_channel(struct Service *service)
{
    --service->channel;
    service->adjustment /= channel_scale;
    switch (service->current[service->channel]) {
    case 2:
        service->saved = service->value;
        service->value = service->channel_values[service->channel];
        break;
    case 0:
        service->saved = service->value;
        break;
    }
    service->changed = 1;
}
