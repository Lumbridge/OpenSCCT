struct Array { void *data; int count; unsigned capacity_flags; };
struct Array *__fastcall initialize_array(struct Array *self)
{
    self->data = 0;
    self->count = 0;
    self->capacity_flags = 0x80000000u;
    return self;
}
