#include "versus_script_frame.h"

struct EntryLevel {
    unsigned char unknown[0x2c];
    void **actors;
};
struct GameEngine {
    unsigned char unknown[0x40];
    void *online_service;
    unsigned char before_entry[0xf0];
    struct EntryLevel *entry_level;
};
struct PlayerLevel {
    unsigned char unknown[0xf0];
    struct GameEngine *engine;
};
struct PlayerActor {
    unsigned char unknown[0x1a4];
    struct PlayerLevel *level;
};
struct Viewport {
    unsigned char unknown[0x30];
    struct PlayerActor *actor;
};
struct GuiPlayer {
    unsigned char unknown[0x2c];
    struct Viewport *viewport;
};

void __fastcall versus_entry_level_actor(struct PlayerActor *self, unsigned reserved_edx,
                                         struct VersusScriptFrame *frame, void **result) {
    ++frame->code;
    *result = self->level->engine->entry_level->actors[0];
}

void __fastcall versus_gui_online_available(struct GuiPlayer *self, unsigned reserved_edx,
                                            struct VersusScriptFrame *frame, int *result) {
    ++frame->code;
    *result = self->viewport->actor->level->engine->online_service != 0;
}
