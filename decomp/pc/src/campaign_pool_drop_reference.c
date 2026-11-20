struct Block { void *first, *limit; unsigned references, reserved; };
struct Blocks { unsigned reserved; struct Block *begin, *end, *capacity; };
struct Pointer { void *value; };
extern void __cdecl release_buffer(void *);

extern void __fastcall pop_1dd960(struct Blocks *);
extern void __fastcall pop_213470(struct Blocks *);
extern void __fastcall pop_213c90(struct Blocks *);
extern void __fastcall pop_214010(struct Blocks *);
extern void __fastcall pop_427bd0(struct Blocks *);
extern void __fastcall pop_427cc0(struct Blocks *);
extern void __fastcall pop_427db0(struct Blocks *);
extern void __fastcall pop_427ea0(struct Blocks *);
extern void __fastcall pop_4280a0(struct Blocks *);
extern void __fastcall pop_42fb30(struct Blocks *);
extern void __fastcall pop_431050(struct Blocks *);
extern void __fastcall pop_432b90(struct Blocks *);
extern void __fastcall pop_434280(struct Blocks *);
extern void __fastcall pop_435480(struct Blocks *);
extern void __fastcall pop_437e50(struct Blocks *);
extern void __fastcall pop_438970(struct Blocks *);
extern void __fastcall pop_438dd0(struct Blocks *);
extern void __fastcall pop_43a550(struct Blocks *);
extern void __fastcall pop_43a6a0(struct Blocks *);
extern void __fastcall pop_43a7f0(struct Blocks *);
extern void __fastcall pop_44e290(struct Blocks *);
extern void __fastcall pop_44eb10(struct Blocks *);
extern void __fastcall pop_44edc0(struct Blocks *);
extern void __fastcall pop_459ab0(struct Blocks *);
extern void __fastcall pop_45b9a0(struct Blocks *);
extern void __fastcall pop_45d8f0(struct Blocks *);
extern void __fastcall pop_460790(struct Blocks *);
extern void __fastcall pop_461210(struct Blocks *);
extern void __fastcall pop_4621c0(struct Blocks *);
extern void __fastcall pop_462500(struct Blocks *);
extern void __fastcall pop_463bf0(struct Blocks *);
extern void __fastcall pop_463ef0(struct Blocks *);
extern void __fastcall pop_464260(struct Blocks *);
extern void __fastcall pop_464660(struct Blocks *);
extern void __fastcall pop_4650d0(struct Blocks *);
extern void __fastcall pop_465ee0(struct Blocks *);
extern void __fastcall pop_4661c0(struct Blocks *);
extern void __fastcall pop_466750(struct Blocks *);

#define DROP_REFERENCE(Entry, Pop) \
    void __fastcall Entry(struct Blocks *blocks, struct Pointer pointer) \
    { \
        if (pointer.value) { \
            struct Block *last = blocks->end - 1; \
            unsigned remaining = last->references - 1; \
            last->references = remaining; \
            if (remaining) return; \
            last = blocks->end - 1; \
            release_buffer(*(void * const volatile *)&last->first); \
            Pop(blocks); \
        } \
    }

DROP_REFERENCE(drop_1dd920, pop_1dd960)
DROP_REFERENCE(drop_213430, pop_213470)
DROP_REFERENCE(drop_213c50, pop_213c90)
DROP_REFERENCE(drop_213fd0, pop_214010)
DROP_REFERENCE(drop_427b90, pop_427bd0)
DROP_REFERENCE(drop_427c80, pop_427cc0)
DROP_REFERENCE(drop_427d70, pop_427db0)
DROP_REFERENCE(drop_427e60, pop_427ea0)
DROP_REFERENCE(drop_428060, pop_4280a0)
DROP_REFERENCE(drop_42faf0, pop_42fb30)
DROP_REFERENCE(drop_431010, pop_431050)
DROP_REFERENCE(drop_432b50, pop_432b90)
DROP_REFERENCE(drop_434240, pop_434280)
DROP_REFERENCE(drop_435440, pop_435480)
DROP_REFERENCE(drop_437e10, pop_437e50)
DROP_REFERENCE(drop_438930, pop_438970)
DROP_REFERENCE(drop_438d90, pop_438dd0)
DROP_REFERENCE(drop_43a510, pop_43a550)
DROP_REFERENCE(drop_43a660, pop_43a6a0)
DROP_REFERENCE(drop_43a7b0, pop_43a7f0)
DROP_REFERENCE(drop_44e250, pop_44e290)
DROP_REFERENCE(drop_44ead0, pop_44eb10)
DROP_REFERENCE(drop_44ed80, pop_44edc0)
DROP_REFERENCE(drop_459a70, pop_459ab0)
DROP_REFERENCE(drop_45b960, pop_45b9a0)
DROP_REFERENCE(drop_45d8b0, pop_45d8f0)
DROP_REFERENCE(drop_460750, pop_460790)
DROP_REFERENCE(drop_4611d0, pop_461210)
DROP_REFERENCE(drop_462180, pop_4621c0)
DROP_REFERENCE(drop_4624c0, pop_462500)
DROP_REFERENCE(drop_463bb0, pop_463bf0)
DROP_REFERENCE(drop_463eb0, pop_463ef0)
DROP_REFERENCE(drop_464220, pop_464260)
DROP_REFERENCE(drop_464620, pop_464660)
DROP_REFERENCE(drop_465090, pop_4650d0)
DROP_REFERENCE(drop_465ea0, pop_465ee0)
DROP_REFERENCE(drop_466180, pop_4661c0)
DROP_REFERENCE(drop_466710, pop_466750)
