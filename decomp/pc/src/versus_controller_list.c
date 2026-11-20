#include "versus_script_frame.h"

struct Level {
    unsigned char unknown[0x510];
    struct Controller *first_controller;
};

struct Controller {
    unsigned char unknown[0x1a0];
    struct Level *level;
    unsigned char gap[0x180];
    struct Controller *next_controller;
};

void __fastcall versus_add_controller(struct Controller *self, unsigned reserved_edx,
                                      struct VersusScriptFrame *stack, void *result) {
    struct Level *level;
    ++stack->code;
    level = self->level;
    self->next_controller = level->first_controller;
    level->first_controller = self;
}

void __fastcall versus_remove_controller(struct Controller *self, unsigned reserved_edx,
                                         struct VersusScriptFrame *stack, void *result) {
    struct Controller *node;
    struct Level *level;
    ++stack->code;
    level = self->level;
    node = level->first_controller;
    if (node == self) {
        level->first_controller = node->next_controller;
        return;
    }
    for (; node; node = node->next_controller) {
        if (node->next_controller == self) {
            node->next_controller = self->next_controller;
            return;
        }
    }
}
