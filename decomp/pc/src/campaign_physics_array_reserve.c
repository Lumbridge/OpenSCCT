struct Array { void *data; int count; unsigned capacity; };
struct Request { int count; };
extern void __cdecl grow(struct Array *, int, int);

#define RESERVE_ARRAY(Entry, Width) \
    void __fastcall Entry(struct Array *array, struct Request request) \
    { \
        int capacity = (int)(array->capacity & 0x7fffffffu); \
        if (capacity < request.count) { \
            int doubled = (int)((unsigned)capacity * 2); \
            grow(array, request.count < doubled ? doubled : request.count, Width); \
        } \
    }

RESERVE_ARRAY(reserve_108b20, 4)
RESERVE_ARRAY(reserve_1b81c0, 16)
RESERVE_ARRAY(reserve_1b81f0, 16)
RESERVE_ARRAY(reserve_4fd480, 4)
RESERVE_ARRAY(reserve_4ff5c0, 4)
RESERVE_ARRAY(reserve_501070, 1)
RESERVE_ARRAY(reserve_504520, 4)
RESERVE_ARRAY(reserve_506e00, 4)
RESERVE_ARRAY(reserve_507d90, 12)
RESERVE_ARRAY(reserve_50ad20, 48)
RESERVE_ARRAY(reserve_50daa0, 8)
RESERVE_ARRAY(reserve_50db00, 4)
RESERVE_ARRAY(reserve_50db30, 4)
RESERVE_ARRAY(reserve_512e30, 48)
RESERVE_ARRAY(reserve_5175b0, 4)
RESERVE_ARRAY(reserve_52f510, 2)
RESERVE_ARRAY(reserve_531c00, 4)
RESERVE_ARRAY(reserve_533dd0, 8)
RESERVE_ARRAY(reserve_535150, 12)
RESERVE_ARRAY(reserve_538e40, 8)
RESERVE_ARRAY(reserve_551f10, 16)
RESERVE_ARRAY(reserve_551f70, 8)
RESERVE_ARRAY(reserve_552090, 4)
RESERVE_ARRAY(reserve_552cb0, 4)
RESERVE_ARRAY(reserve_552ce0, 4)
RESERVE_ARRAY(reserve_552d10, 4)
RESERVE_ARRAY(reserve_5530d0, 4)
RESERVE_ARRAY(reserve_553390, 4)
RESERVE_ARRAY(reserve_5535f0, 8)
RESERVE_ARRAY(reserve_553920, 32)
RESERVE_ARRAY(reserve_5630b0, 8)
RESERVE_ARRAY(reserve_564b90, 8)
RESERVE_ARRAY(reserve_56dd70, 28)
RESERVE_ARRAY(reserve_56dde0, 32)
RESERVE_ARRAY(reserve_570f90, 8)
RESERVE_ARRAY(reserve_573320, 4)
RESERVE_ARRAY(reserve_579a50, 12)
RESERVE_ARRAY(reserve_57b2f0, 64)
RESERVE_ARRAY(reserve_57b320, 4)
RESERVE_ARRAY(reserve_57b350, 80)
RESERVE_ARRAY(reserve_592590, 8)
RESERVE_ARRAY(reserve_5928b0, 32)
RESERVE_ARRAY(reserve_592960, 16)
RESERVE_ARRAY(reserve_5981c0, 8)
RESERVE_ARRAY(reserve_59a870, 28)
RESERVE_ARRAY(reserve_59a8a0, 48)
RESERVE_ARRAY(reserve_59f9f0, 1)
RESERVE_ARRAY(reserve_5a4790, 192)
RESERVE_ARRAY(reserve_6ee470, 12)
RESERVE_ARRAY(reserve_6f4330, 4)
RESERVE_ARRAY(reserve_6f43d0, 16)
RESERVE_ARRAY(reserve_6fe550, 48)
RESERVE_ARRAY(reserve_6fe950, 4)
RESERVE_ARRAY(reserve_6fe980, 4)
RESERVE_ARRAY(reserve_6fe9b0, 1)
RESERVE_ARRAY(reserve_6fe9e0, 4)
RESERVE_ARRAY(reserve_6fea10, 4)
RESERVE_ARRAY(reserve_6ffdd0, 32)
RESERVE_ARRAY(reserve_719000, 8)
RESERVE_ARRAY(reserve_791980, 8)
