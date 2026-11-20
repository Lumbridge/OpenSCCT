struct mesh_instance;
struct notify_object;
struct sequence_notify {
    float time;
    int name_index;
    struct notify_object *object;
};
struct animation_sequence {
    int name;
    const int *groups;
    int group_count, group_capacity;
    int start_frame, frame_count;
    float rate;
    const struct sequence_notify *notifies;
    int notify_count;
};
struct name_entry { unsigned char prefix[0xc]; unsigned char value[1]; };
#define zero_value (*(const float *)0x10c109f8u)
#define name_entries (*(const struct name_entry *const *const *)0x10cc9e5cu)

int *__fastcall sequence_name(const struct mesh_instance *self, void *unused, int *result, const struct animation_sequence *clip)
{
    if (clip) {
        *result = clip->name;
        return result;
    }
    *result = 0;
    return result;
}

int *__fastcall sequence_first_group(const struct mesh_instance *self, void *unused, int *result, const struct animation_sequence *clip)
{
    if (clip->group_count) {
        *result = clip->groups[0];
        return result;
    }
    *result = 0;
    return result;
}

float __fastcall sequence_frame_count(const struct mesh_instance *self, void *unused, const struct animation_sequence *clip)
{
    return clip ? (float)clip->frame_count : zero_value;
}

float __fastcall sequence_rate(const struct mesh_instance *self, void *unused, const struct animation_sequence *clip)
{
    return clip ? clip->rate : zero_value;
}

int __fastcall sequence_notify_count(const struct mesh_instance *self, void *unused, const struct animation_sequence *clip)
{
    return clip ? clip->notify_count : 0;
}

float __fastcall sequence_notify_time(const struct mesh_instance *self, void *unused, const struct animation_sequence *clip, int index)
{
    return clip ? clip->notifies[index].time : zero_value;
}

const void *__fastcall sequence_notify_name_value(const struct mesh_instance *self, void *unused, const struct animation_sequence *clip, int index)
{
    return clip ? name_entries[clip->notifies[index].name_index]->value : 0;
}

struct notify_object *__fastcall sequence_notify_object(const struct mesh_instance *self, void *unused, const struct animation_sequence *clip, int index)
{
    return clip ? clip->notifies[index].object : 0;
}
