#include "versus_script_frame.h"

struct GuiController;
struct ProfileCollection {
    unsigned char unknown[0x78];
    int count;
};

/* Addresses are data in the fixed Steam 252084 image. The request flags have
 * both readers and clear operations in the GUI controller native table. */
#define JOIN_IP_REQUEST (*(int *)0x10c92328u)
#define CREATE_SERVER_REQUEST (*(int *)0x10c923b0u)
#define DEFAULT_SERVER_PORT (*(int *)0x10c922acu)
#define SHUTDOWN_REQUEST (*(int *)0x10c96918u)
#define PROFILES (*(struct ProfileCollection **)0x10c924e8u)

/* The direct request getters fetch the caller's result pointer from its VM
 * stack argument before reading the fixed-image global. */
void __fastcall versus_gui_join_ip_requested(struct GuiController *self, unsigned reserved_edx,
                                             struct VersusScriptFrame *stack, int *volatile result) {
    ++stack->code;
    *result = JOIN_IP_REQUEST;
}
void __fastcall versus_gui_create_server_requested(struct GuiController *self, unsigned reserved_edx,
                                                   struct VersusScriptFrame *stack, int *volatile result) {
    ++stack->code;
    *result = CREATE_SERVER_REQUEST;
}
void __fastcall versus_gui_auto_validate(struct GuiController *self, unsigned reserved_edx,
                                         struct VersusScriptFrame *stack, int *result) {
    ++stack->code;
    *result = JOIN_IP_REQUEST || CREATE_SERVER_REQUEST;
}
void __fastcall versus_gui_clear_auto_validate(struct GuiController *self, unsigned reserved_edx,
                                               struct VersusScriptFrame *stack, void *result) {
    ++stack->code;
    JOIN_IP_REQUEST = 0;
    CREATE_SERVER_REQUEST = 0;
}
void __fastcall versus_gui_clear_join_ip(struct GuiController *self, unsigned reserved_edx,
                                         struct VersusScriptFrame *stack, void *result) {
    ++stack->code;
    JOIN_IP_REQUEST = 0;
}
void __fastcall versus_gui_clear_create_server(struct GuiController *self, unsigned reserved_edx,
                                               struct VersusScriptFrame *stack, void *result) {
    ++stack->code;
    CREATE_SERVER_REQUEST = 0;
}
void __fastcall versus_gui_default_server_port(struct GuiController *self, unsigned reserved_edx,
                                               struct VersusScriptFrame *stack, int *volatile result) {
    ++stack->code;
    *result = DEFAULT_SERVER_PORT;
}
void __fastcall versus_gui_shutdown(struct GuiController *self, unsigned reserved_edx,
                                    struct VersusScriptFrame *stack, void *result) {
    ++stack->code;
    SHUTDOWN_REQUEST = 1;
}
void __fastcall versus_gui_profile_count(struct GuiController *self, unsigned reserved_edx,
                                         struct VersusScriptFrame *stack, int *result) {
    ++stack->code;
    if (PROFILES) {
        *result = PROFILES->count;
    } else {
        *result = 0;
    }
}
