#include "versus_vm_argument.h"

struct HudTarget;
struct Hud {
    unsigned char before_lock[0x548];
    struct HudTarget *locked;
};
struct ServerPreferences {
    unsigned char before_port[0x154];
    int port;
};
#define AIMED_TARGET (*(struct HudTarget **)0x10c921f8u)
#define SHOW_MENU (*(unsigned char *)0x10c43a12u)
#define DEFAULT_PORT (*(int *)0x10c922acu)
#define PREFERENCES (*(struct ServerPreferences **)0x10c923c4u)

void __fastcall versus_toggle_binocular_lock(struct Hud *self, unsigned reserved_edx,
                                             struct ArgumentFrame *frame, void *result) {
    struct HudTarget *target;
    ++frame->code;
    target = AIMED_TARGET;
    self->locked = self->locked == target ? 0 : target;
}
void __fastcall versus_set_should_show_menu(void *self, unsigned reserved_edx,
                                            struct ArgumentFrame *frame, void *result) {
    int show = 0;
    read_argument(frame, &show);
    ++frame->code;
    SHOW_MENU = (show != 0);
}
void __fastcall versus_get_should_show_menu(void *self, unsigned reserved_edx,
                                            struct ArgumentFrame *frame, int *result) {
    ++frame->code;
    *result = SHOW_MENU != 0;
}
void __fastcall versus_set_default_port(void *self, unsigned reserved_edx,
                                        struct ArgumentFrame *frame, void *result) {
    struct ServerPreferences *preferences;
    int port = 0;
    int selected_port;
    read_argument(frame, &port);
    ++frame->code;
    selected_port = port;
    preferences = PREFERENCES;
    DEFAULT_PORT = selected_port;
    if (preferences) {
        preferences->port = selected_port;
    }
}
