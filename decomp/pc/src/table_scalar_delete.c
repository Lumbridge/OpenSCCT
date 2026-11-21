struct Object;
struct DeleteFlags { unsigned value; };

extern void __fastcall destroy_object(struct Object *object);
extern void __cdecl release_storage(struct Object *object);

struct Object *__fastcall delete_object(struct Object *object, struct DeleteFlags flags)
{
    destroy_object(object);
    if (flags.value & 1)
        release_storage(object);
    return object;
}
