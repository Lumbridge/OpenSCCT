struct State {
    const void *methods;
    unsigned reserved_04[6];
    unsigned marker;
    unsigned reserved_20[7];
    unsigned pair_3c[2];
    unsigned reserved_44[15];
    unsigned pair_80[2];
    unsigned reserved_88[3];
    unsigned fields_94[9];
    unsigned reserved_b8[4];
    unsigned fields_c8[15];
};

extern const void *const state_methods[];

struct State *__fastcall initialize_state(struct State *state, float unused)
{
    state->marker = 0xdeadc0deu;
    state->pair_3c[0] = 0;
    state->pair_3c[1] = 0;
    state->pair_80[0] = 0;
    state->pair_80[1] = 0;
    state->methods = state_methods;
    state->fields_94[0] = 0;
    state->fields_94[1] = 0;
    state->fields_94[2] = 0;
    state->fields_94[3] = 0;
    state->fields_94[4] = 0;
    state->fields_94[5] = 0;
    state->fields_94[6] = 0;
    state->fields_94[7] = 0;
    state->fields_94[8] = 0;
    state->fields_c8[0] = 0;
    state->fields_c8[1] = 0;
    state->fields_c8[2] = 0;
    state->fields_c8[3] = 0;
    state->fields_c8[4] = 0;
    state->fields_c8[5] = 0;
    state->fields_c8[6] = 0;
    state->fields_c8[7] = 0;
    state->fields_c8[8] = 0;
    state->fields_c8[9] = 0;
    state->fields_c8[10] = 0;
    state->fields_c8[11] = 0;
    state->fields_c8[12] = 0;
    state->fields_c8[13] = 0;
    state->fields_c8[14] = 0;
    return state;
}
