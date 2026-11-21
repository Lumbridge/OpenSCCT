/* Microsoft C++ 7.1 basic_string<char> storage and its scalar deleting destructors.
   tidy() follows basic_string::_Tidy(true): release a heap buffer, restore the
   small-string capacity, then terminate through _Myptr() as _Eos does. */
struct StringStorage {
    unsigned char allocator;
    union { char buffer[16]; char *pointer; } bx;
    unsigned size;
    unsigned reserved;
};
struct NamedRecord {
    unsigned header[3];
    struct StringStorage name;
};
struct Flags { unsigned value; };
extern void __cdecl release(void *);
extern void __cdecl release_record(void *);

static __inline void tidy(struct StringStorage *text)
{
    if (text->reserved >= 16) release(text->bx.pointer);
    text->reserved = 15;
    text->size = 0;
    *(text->reserved < 16 ? text->bx.buffer : text->bx.pointer) = 0;
}

struct StringStorage *__fastcall delete_string(struct StringStorage *self, struct Flags flags)
{
    tidy(self);
    if (flags.value & 1) release(self);
    return self;
}

struct NamedRecord *__fastcall delete_named_record(struct NamedRecord *self, struct Flags flags)
{
    tidy(&self->name);
    if (flags.value & 1) release_record(self);
    return self;
}
