struct Object;
struct ObjectOps {
    void *reserved[9];
    void (__fastcall *destroy)(struct Object *);
};
struct Object {
    const struct ObjectOps *ops;
    int index;
    unsigned char before_flags[0x1c - 8];
    unsigned flags;
};

int __fastcall conditional_destroy(struct Object *object)
{
    if (object->index != -1 && !(object->flags & 0x00800000u)) {
        object->flags = (object->flags & ~0x80800000u) | 0x00800000u;
        object->ops->destroy(object);
        return 1;
    }
    return 0;
}
