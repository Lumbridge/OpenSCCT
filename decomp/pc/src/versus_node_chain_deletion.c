/* Chain node whose deleting destructor deletes the next node through itself.
   VC 7.1 C __fastcall stack-passes float parameters; the recursive call uses that
   form so IntegerStackWord can supply the constant delete flag as a pushed word. */
typedef union { unsigned int value; float stack_word; } IntegerStackWord;
struct Allocation { void *object; };
struct Flags { unsigned value; };
struct Allocator;
struct AllocatorMethods { void *reserved[2]; void (__fastcall *release)(struct Allocator *, struct Allocation); };
struct Allocator { const struct AllocatorMethods *methods; };
struct Node { const void *methods; unsigned fields[6]; struct Node *next; };
typedef struct Node *(__fastcall *DeleteNodeWord)(struct Node *, float flags);
extern const void *const node_methods[];
extern struct Allocator *allocator;
struct Node *__fastcall delete_node(struct Node *self, struct Flags flags)
{
    self->methods = node_methods;
    if (self->next) {
        static const IntegerStackWord free_memory = { 1 };
        ((DeleteNodeWord)delete_node)(self->next, free_memory.stack_word);
    }
    if (flags.value & 1) {
        struct Allocation allocation;
        allocation.object = self;
        allocator->methods->release(allocator, allocation);
    }
    return self;
}
