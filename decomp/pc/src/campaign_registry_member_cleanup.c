struct Object { const void *methods; void *reserved, *allocation; };
struct Argument { struct Object *ptr; };

extern unsigned char registry_a1b188[];
extern const void *const object_methods_83c530[];
extern const void *const object_methods_83c548[];
extern const void *const object_methods_83c698[];
extern const void *const object_methods_83df68[];
extern const void *const object_methods_83e074[];
extern const void *const object_methods_83fe3c[];
extern const void *const object_methods_83fe68[];
extern const void *const object_methods_845238[];
extern const void *const object_methods_848c20[];
extern const void *const object_methods_8494d0[];
extern void __fastcall unregister_object_4ad480(void *,struct Argument);
extern void *__cdecl prepare_release_48b460(void *, unsigned);
extern void __cdecl release_prepared_48b550(void *);
extern void __fastcall cleanup_parent_4ad370(struct Object *);

#define CLEANUP(Entry, Category, Registry, Methods, Unregister, Prepare, Release, Parent) \
    void __fastcall Entry(struct Object *self) \
    { \
        struct Argument arg; \
        self->methods = Methods; \
        arg.ptr = self; \
        Unregister(Registry, arg); \
        if (self->allocation) { \
            Release(Prepare(self->allocation, Category)); \
            self->allocation = 0; \
        } \
        Parent(self); \
    }

CLEANUP(cleanup_47e8d0, 10, registry_a1b188, object_methods_83c530, unregister_object_4ad480, prepare_release_48b460, release_prepared_48b550, cleanup_parent_4ad370)
CLEANUP(cleanup_47e9c0, 10, registry_a1b188, object_methods_83c548, unregister_object_4ad480, prepare_release_48b460, release_prepared_48b550, cleanup_parent_4ad370)
CLEANUP(cleanup_480ab0, 10, registry_a1b188, object_methods_83c698, unregister_object_4ad480, prepare_release_48b460, release_prepared_48b550, cleanup_parent_4ad370)
CLEANUP(cleanup_498880, 10, registry_a1b188, object_methods_83df68, unregister_object_4ad480, prepare_release_48b460, release_prepared_48b550, cleanup_parent_4ad370)
CLEANUP(cleanup_49a930, 10, registry_a1b188, object_methods_83e074, unregister_object_4ad480, prepare_release_48b460, release_prepared_48b550, cleanup_parent_4ad370)
CLEANUP(cleanup_4b4b40, 10, registry_a1b188, object_methods_83fe3c, unregister_object_4ad480, prepare_release_48b460, release_prepared_48b550, cleanup_parent_4ad370)
CLEANUP(cleanup_4b4f50, 10, registry_a1b188, object_methods_83fe68, unregister_object_4ad480, prepare_release_48b460, release_prepared_48b550, cleanup_parent_4ad370)
CLEANUP(cleanup_4d6de0, 10, registry_a1b188, object_methods_845238, unregister_object_4ad480, prepare_release_48b460, release_prepared_48b550, cleanup_parent_4ad370)
CLEANUP(cleanup_4f0a00, 10, registry_a1b188, object_methods_848c20, unregister_object_4ad480, prepare_release_48b460, release_prepared_48b550, cleanup_parent_4ad370)
CLEANUP(cleanup_4f76e0, 10, registry_a1b188, object_methods_8494d0, unregister_object_4ad480, prepare_release_48b460, release_prepared_48b550, cleanup_parent_4ad370)
