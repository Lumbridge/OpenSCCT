#include "versus_script_frame.h"

struct Engine;
struct EngineMethods {
    void *before_disconnect[26];
    void (__fastcall *disconnect)(struct Engine *);
};
struct Engine {
    const struct EngineMethods *methods;
};
struct Level {
    unsigned char before_engine[0xf0];
    struct Engine *engine;
};
struct Player {
    unsigned char before_level[0x1a4];
    struct Level *level;
};
void __fastcall versus_player_disconnect(struct Player *self, unsigned reserved_edx,
                                         struct VersusScriptFrame *frame, void *result) {
    struct Engine *engine;
    ++frame->code;
    engine = self->level->engine;
    engine->methods->disconnect(engine);
}
