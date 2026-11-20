struct Object { const void *methods; };
struct Argument { struct Object *ptr; };
struct Container { unsigned reserved; void *buffer; };
extern unsigned char registry[];
extern const void *const object_methods[];
extern void __fastcall unregister_object(void *, struct Argument);
extern void __fastcall cleanup_contents(struct Object *);
extern void __fastcall clear_container(struct Container *);
struct Buffer { void *ptr; };
extern void *__cdecl prepare_release(struct Buffer, unsigned);
extern void __cdecl release_prepared(void *);
extern void __fastcall cleanup_parent(struct Object *);
static __forceinline void destroy_container(struct Container *container)
{
    clear_container(container);
    {
        struct Buffer buffer = { container->buffer };
        release_prepared(prepare_release(buffer, 7));
    }
    container->buffer = 0;
}
void __fastcall cleanup(struct Object *self)
{
    struct Argument arg;
    self->methods = object_methods;
    arg.ptr = self;
    unregister_object(registry, arg);
    cleanup_contents(self);
    destroy_container((struct Container *)((unsigned char *)self + 28));
    destroy_container((struct Container *)((unsigned char *)self + 12));
    cleanup_parent(self);
}
