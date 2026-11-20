struct Context;
struct CriticalSection;
struct Mutex { struct CriticalSection *section; };
struct Context {
    const void *methods;
    unsigned char member[36];
    unsigned field_28;
    const unsigned short *name;
    unsigned field_30;
    struct Mutex mutex;
    unsigned field_38;
};
struct Label { const unsigned short *ptr; };
union StackWord { unsigned value; float argument; };
extern const void *const manager_methods[];
extern unsigned char bypass_lock;
extern const unsigned short mutex_file[];
extern struct Context *__cdecl get_context(void);
extern void *__cdecl allocate_block(struct Context *, unsigned, const unsigned short *, unsigned, unsigned);
extern void __cdecl release_block(struct Context *, void *, unsigned);
extern void __fastcall initialize_member(void *, struct Label, float);
extern void __fastcall destroy_member(void *);
__declspec(dllimport) void __stdcall EnterCriticalSection(struct CriticalSection *);
__declspec(dllimport) void __stdcall LeaveCriticalSection(struct CriticalSection *);
__declspec(dllimport) void __stdcall InitializeCriticalSection(struct CriticalSection *);
__declspec(dllimport) void __stdcall DeleteCriticalSection(struct CriticalSection *);

__declspec(noinline) void __fastcall lock_mutex(struct Mutex *mutex)
{
    EnterCriticalSection(mutex->section);
}
__declspec(noinline) void __fastcall unlock_mutex(struct Mutex *mutex)
{
    LeaveCriticalSection(mutex->section);
}
__declspec(noinline) struct Mutex *__fastcall create_mutex(struct Mutex *mutex)
{
    mutex->section = allocate_block(get_context(), 24, mutex_file, 47, 8);
    InitializeCriticalSection(mutex->section);
    return mutex;
}
static __forceinline void release_section(struct CriticalSection *section)
{
    release_block(get_context(), section, 8);
}
__declspec(noinline) void __fastcall destroy_mutex(struct Mutex *mutex)
{
    DeleteCriticalSection(mutex->section);
    release_section(mutex->section);
}
struct Context *__fastcall construct_context(struct Context *context, struct Label label)
{
    static const union StackWord interval = {60000};
    context->methods = manager_methods;
    initialize_member(context->member, label, interval.argument);
    create_mutex(&context->mutex);
    context->name = label.ptr;
    context->field_28 = 0;
    context->field_30 = 0;
    context->field_38 = 0;
    return context;
}
void __fastcall enter_context(struct Context *context)
{
    if (!bypass_lock)
        lock_mutex(&context->mutex);
}
void __fastcall leave_context(struct Context *context)
{
    if (!bypass_lock)
        unlock_mutex(&context->mutex);
}
void __fastcall destroy_context(struct Context *context)
{
    context->methods = manager_methods;
    destroy_mutex(&context->mutex);
    destroy_member(context->member);
}
void *__cdecl allocate_context(unsigned bytes, const unsigned short *file, unsigned line)
{
    return allocate_block(get_context(), bytes, file, line, 3);
}
