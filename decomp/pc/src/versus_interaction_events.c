#include "versus_vm_argument.h"

struct Interaction {
    unsigned char before_priority[0x300];
    int priority;
    unsigned char before_flags[0x24];
    unsigned flags;
};
static __inline int compare_priority(const struct Interaction *first, const struct Interaction *second) {
    if (second == first) {
        return 0;
    }
    return second->priority >= first->priority ? 3 : 1;
}
void __fastcall versus_compare_interactions(void *self, unsigned reserved_edx,
                                            struct ArgumentFrame *frame, int *result) {
    struct Interaction *first = 0;
    struct Interaction *second;
    read_argument(frame, &first);
    second = 0;
    read_argument(frame, &second);
    ++frame->code;
    *result = compare_priority(first, second);
}
void __fastcall versus_post_interact(struct Interaction *self, unsigned reserved_edx,
                                     struct ArgumentFrame *frame, void *result) {
    void *instigator = 0;
    int subinteraction;
    read_argument(frame, &instigator);
    subinteraction = 0;
    read_argument(frame, &subinteraction);
    ++frame->code;
    self->flags &= ~1u;
}
