struct SharedObject {
    unsigned first_word;
    unsigned references;
};

void __cdecl fill_references(struct SharedObject **destination, unsigned count,
                             struct SharedObject *const *source, void *allocator,
                             struct SharedObject *const *type_hint)
{
    for (; count > 0; --count, ++destination) {
        if (destination) {
            struct SharedObject *object = *source;
            *destination = object;
            if (object)
                ++object->references;
        }
    }
}

struct Iterator { struct SharedObject *const *position; };

struct SharedObject **__cdecl copy_references(struct Iterator first, struct Iterator last,
                                              struct SharedObject **destination, void *allocator,
                                              struct SharedObject *const *type_hint)
{
    for (; first.position != last.position; ++first.position, ++destination) {
        if (destination) {
            struct SharedObject *object = *first.position;
            *destination = object;
            if (object)
                ++object->references;
        }
    }
    return destination;
}
