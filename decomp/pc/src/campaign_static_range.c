struct BufferRange { void *begin, *end, *limit; };
extern void __cdecl release_engine_buffer(void *buffer);

#define CLEANUP(Entry, Object) \
void Entry(void) \
{ \
    if (Object.begin) release_engine_buffer(Object.begin); \
    Object.begin = 0; \
    Object.end = 0; \
    Object.limit = 0; \
}

extern struct BufferRange static_range_90b6c8;
extern struct BufferRange static_range_90b734;
extern struct BufferRange static_range_923a08;
extern struct BufferRange static_range_923a1c;
extern struct BufferRange static_range_92d3e8;
extern struct BufferRange static_range_a17b0c;
extern struct BufferRange static_range_a17b48;
extern struct BufferRange static_range_a17b84;
extern struct BufferRange static_range_a17bc0;
extern struct BufferRange static_range_a17c28;
extern struct BufferRange static_range_a17c64;
extern struct BufferRange static_range_a17c78;
extern struct BufferRange static_range_a17cb8;
extern struct BufferRange static_range_a17cf4;
extern struct BufferRange static_range_a17e04;
extern struct BufferRange static_range_a17e68;
extern struct BufferRange static_range_a17ea4;
extern struct BufferRange static_range_a17eb8;
extern struct BufferRange static_range_a17ecc;
extern struct BufferRange static_range_a17ee0;
extern struct BufferRange static_range_a17ef4;
extern struct BufferRange static_range_a17fe8;
extern struct BufferRange static_range_a18024;
extern struct BufferRange static_range_a1a0b4;
extern struct BufferRange static_range_a1a0f0;
extern struct BufferRange static_range_a1a12c;
extern struct BufferRange static_range_a1a16c;
extern struct BufferRange static_range_a1a1a8;
extern struct BufferRange static_range_a1a1e4;
extern struct BufferRange static_range_a1a220;
extern struct BufferRange static_range_a1a25c;
extern struct BufferRange static_range_a1a298;
extern struct BufferRange static_range_a1a2d4;
extern struct BufferRange static_range_a1a310;
extern struct BufferRange static_range_a1a34c;
extern struct BufferRange static_range_a1a388;
extern struct BufferRange static_range_a1a3c4;
extern struct BufferRange static_range_a1a400;
extern struct BufferRange static_range_a1a48c;
extern struct BufferRange static_range_a1a4c8;
extern struct BufferRange static_range_a1a504;

CLEANUP(cleanup_range_7b12c0, static_range_90b6c8)
CLEANUP(cleanup_range_7b1300, static_range_90b734)
CLEANUP(cleanup_range_7b1510, static_range_923a08)
CLEANUP(cleanup_range_7b1550, static_range_923a1c)
CLEANUP(cleanup_range_7b1590, static_range_92d3e8)
CLEANUP(cleanup_range_7b3a20, static_range_a17b0c)
CLEANUP(cleanup_range_7b3a60, static_range_a17b48)
CLEANUP(cleanup_range_7b3aa0, static_range_a17b84)
CLEANUP(cleanup_range_7b3ae0, static_range_a17bc0)
CLEANUP(cleanup_range_7b3b20, static_range_a17c28)
CLEANUP(cleanup_range_7b3b60, static_range_a17c64)
CLEANUP(cleanup_range_7b3ba0, static_range_a17c78)
CLEANUP(cleanup_range_7b3be0, static_range_a17cb8)
CLEANUP(cleanup_range_7b3c20, static_range_a17cf4)
CLEANUP(cleanup_range_7b3ca0, static_range_a17e04)
CLEANUP(cleanup_range_7b3c60, static_range_a17c78)
CLEANUP(cleanup_range_7b3ce0, static_range_a17e68)
CLEANUP(cleanup_range_7b3d20, static_range_a17ea4)
CLEANUP(cleanup_range_7b3d60, static_range_a17eb8)
CLEANUP(cleanup_range_7b3da0, static_range_a17ecc)
CLEANUP(cleanup_range_7b3de0, static_range_a17ee0)
CLEANUP(cleanup_range_7b3e20, static_range_a17ef4)
CLEANUP(cleanup_range_7b3ec0, static_range_a17fe8)
CLEANUP(cleanup_range_7b3f00, static_range_a18024)
CLEANUP(cleanup_range_7b3f40, static_range_a1a0b4)
CLEANUP(cleanup_range_7b3f80, static_range_a1a0f0)
CLEANUP(cleanup_range_7b3fc0, static_range_a1a12c)
CLEANUP(cleanup_range_7b4000, static_range_a1a16c)
CLEANUP(cleanup_range_7b4040, static_range_a1a1a8)
CLEANUP(cleanup_range_7b4080, static_range_a1a1e4)
CLEANUP(cleanup_range_7b40c0, static_range_a1a220)
CLEANUP(cleanup_range_7b4100, static_range_a1a25c)
CLEANUP(cleanup_range_7b4140, static_range_a1a298)
CLEANUP(cleanup_range_7b4180, static_range_a1a2d4)
CLEANUP(cleanup_range_7b41c0, static_range_a1a310)
CLEANUP(cleanup_range_7b4200, static_range_a17e04)
CLEANUP(cleanup_range_7b4240, static_range_a1a34c)
CLEANUP(cleanup_range_7b4280, static_range_a1a388)
CLEANUP(cleanup_range_7b42c0, static_range_a1a3c4)
CLEANUP(cleanup_range_7b4300, static_range_a1a400)
CLEANUP(cleanup_range_7b4340, static_range_a1a48c)
CLEANUP(cleanup_range_7b4380, static_range_a1a4c8)
CLEANUP(cleanup_range_7b43c0, static_range_a1a504)
CLEANUP(cleanup_range_7b4400, static_range_a17e04)
