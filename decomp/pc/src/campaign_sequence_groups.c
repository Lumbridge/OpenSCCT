struct GroupList {
    int *names;
    int count;
    int capacity;
};
struct Clip { int name; struct GroupList groups; };
struct ClipRef { struct Clip *ptr; };
struct Name { int value; };
struct NameRef { const int *ptr; };
struct IndexRef { int *ptr; };

__declspec(noinline) int __fastcall find_group(struct GroupList *groups, struct NameRef name, struct IndexRef index)
{
    for (*index.ptr = 0; *index.ptr < groups->count; ++*index.ptr) {
        if (groups->names[*index.ptr] == *name.ptr)
            return 1;
    }
    return 0;
}

int __fastcall has_sequence_group(void *mesh, struct ClipRef clip, struct Name name)
{
    int found = 0;
    struct IndexRef index;
    struct NameRef target;
    index.ptr = &found;
    target.ptr = &name.value;
    return clip.ptr ? find_group(&clip.ptr->groups, target, index) : 0;
}
