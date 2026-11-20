#include "versus_script_frame.h"

struct InteractionSlotOwner {
    unsigned char unknown[0x2f8];
    void *slots[15];
    int count;
    int selected;
    int current_subinteraction;
};

void __fastcall versus_interaction_count(struct InteractionSlotOwner *self, unsigned reserved_edx,
                                         struct VersusScriptFrame *stack, int *result) {
    ++stack->code;
    *result = self->count;
}

void __fastcall versus_current_subinteraction(struct InteractionSlotOwner *self, unsigned reserved_edx,
                                              struct VersusScriptFrame *stack, int *result) {
    ++stack->code;
    *result = self->current_subinteraction;
}

void __fastcall versus_current_interaction(struct InteractionSlotOwner *self, unsigned reserved_edx,
                                           struct VersusScriptFrame *stack, void * *result) {
    int selected;
    ++stack->code;
    selected = self->selected;
    if (selected >= 0 && selected < 15) {
        *result = self->slots[selected];
    } else {
        *result = 0;
    }
}
