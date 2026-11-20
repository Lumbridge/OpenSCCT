/* This ABI adapter keeps all x86 thiscall stack arguments on the stack.
   A single-word structure is ineligible for a fastcall argument register. */
typedef struct { int value; } MenuWord;
typedef struct { const void *value; } MenuUrl;
/* The dispatch pointer occupies the volatile EDX slot of these C call
   adapters; the original virtual methods consume ECX and stack arguments. */
struct MenuViewport;
struct ViewportControl;
typedef void (__fastcall *ShowMenu)(struct MenuViewport *, const void *dispatch, int type, int page,
                                   int first_flag, int second_flag);
typedef void (__fastcall *ControlCommand)(struct ViewportControl *, const void *dispatch, int command,
                                         int param1, int param2);
struct ViewportMethods {
    void *other[0x100 / 4];
    ShowMenu show_menu;
};
struct ControlMethods {
    void *other[0x94 / 4];
    ControlCommand command;
};
struct ViewportControl {
    const struct ControlMethods *methods;
};
struct MenuViewport {
    const struct ViewportMethods *methods;
    unsigned char unknown[0x30 - 4];
    void *actor;
    unsigned char unknown_34[0x6c - 0x34];
    struct ViewportControl *control;
};
struct EngineClient {
    unsigned char unknown[0x2c];
    struct MenuViewport **viewports;
    int viewport_count;
};
struct MenuEngine {
    unsigned char unknown[0x44];
    struct EngineClient *client;
};
void __fastcall game_load_menus(struct MenuEngine *engine,
                               MenuWord page, MenuWord type, MenuUrl url,
                               MenuWord first_flag, MenuWord second_flag)
{
    int i;
    for (i = 0; i < engine->client->viewport_count; ++i) {
        struct MenuViewport *viewport = engine->client->viewports[i];
        struct ViewportControl *control;
        if (viewport->actor)
            viewport->methods->show_menu(viewport, viewport->methods, type.value, page.value, first_flag.value, second_flag.value);
        control = engine->client->viewports[i]->control;
        if (control)
            control->methods->command(control, control->methods, 17, 0, 0);
    }
}
