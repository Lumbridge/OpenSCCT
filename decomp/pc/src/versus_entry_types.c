struct Handler;
typedef unsigned (__fastcall *GetType)(struct Handler *);
struct Handler { GetType *methods; };
struct Entry { unsigned prefix[11]; struct Handler *handler; };


unsigned char __cdecl eligible_type(const struct Entry *entry)
{
    struct Handler *handler = entry->handler;
    switch (handler->methods[3](handler)) {
    case 0:
    case 4:
    case 6:
    case 9:
        return 0;
    case 1:
    case 2:
    case 3:
    case 7:
    case 8:
    case 10:
        return 1;
    default:
        return 0;
    }
}

unsigned char __cdecl is_type_three(const struct Entry *entry)
{
    struct Handler *handler = entry->handler;
    switch (handler->methods[3](handler)) {
    case 0:
    case 1:
    case 2:
    case 4:
    case 6:
    case 7:
    case 8:
    case 9:
    case 10:
        return 0;
    case 3:
        return 1;
    default:
        return 0;
    }
}
