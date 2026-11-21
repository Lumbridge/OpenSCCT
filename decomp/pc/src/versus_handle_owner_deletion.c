/* Object owning three Win32 handles; each is closed before the object is released. */
__declspec(dllimport) int __stdcall CloseHandle(void *handle);
struct Allocation { void *object; };
struct Flags { unsigned value; };
struct Allocator;
struct AllocatorMethods { void *reserved[2]; void (__fastcall *release)(struct Allocator *, struct Allocation); };
struct Allocator { const struct AllocatorMethods *methods; };
struct HandleOwner {
    const void *methods;
    void *event;
    unsigned field_08;
    void *thread;
    unsigned field_10;
    void *semaphore;
};
extern const void *const handle_owner_methods[];
extern struct Allocator *allocator;
struct HandleOwner *__fastcall delete_handle_owner(struct HandleOwner *self, struct Flags flags)
{
    self->methods = handle_owner_methods;
    CloseHandle(self->thread);
    CloseHandle(self->event);
    CloseHandle(self->semaphore);
    if (flags.value & 1) {
        struct Allocation allocation;
        allocation.object = self;
        allocator->methods->release(allocator, allocation);
    }
    return self;
}
