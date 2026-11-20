struct Word { unsigned value; };
struct Output { struct Word *word; };
struct Context;
struct Handler;

typedef void (__fastcall *Callback1)(struct Handler *, struct Word);
typedef void (__fastcall *Callback2)(struct Handler *, struct Word, struct Word);
typedef void (__fastcall *Callback4)(struct Handler *, struct Word, struct Word, struct Word, struct Word);
typedef void (__fastcall *Callback5)(struct Handler *, struct Word, struct Word, struct Word, struct Word, struct Word);
extern unsigned char __fastcall read_244e30(struct Context *, struct Output, struct Output);
extern unsigned char __fastcall read_2459f0(struct Context *, struct Output, struct Output);
extern unsigned char __fastcall read_245d80(struct Context *, struct Output, struct Output, struct Output, struct Output);
extern unsigned char __fastcall read_2468d0(struct Context *, struct Output, struct Output, struct Output, struct Output, struct Output);
extern unsigned char __fastcall read_246a80(struct Context *, struct Output, struct Output, struct Output, struct Output);
extern unsigned char __fastcall read_246e50(struct Context *, struct Output, struct Output, struct Output, struct Output);
extern unsigned char __fastcall read_2474c0(struct Context *, struct Output, struct Output, struct Output, struct Output);
extern unsigned char __fastcall read_2476e0(struct Context *, struct Output, struct Output, struct Output, struct Output);
extern unsigned char __fastcall read_247b70(struct Context *, struct Output, struct Output, struct Output, struct Output);
extern unsigned char __fastcall read_2483a0(struct Context *, struct Output, struct Output);
extern unsigned char __fastcall read_2485a0(struct Context *, struct Output, struct Output);
extern unsigned char __fastcall read_24d720(struct Context *, struct Output, struct Output);
extern unsigned char __fastcall read_24dfd0(struct Context *, struct Output, struct Output);
extern unsigned char __fastcall read_24e000(struct Context *, struct Output, struct Output);
extern unsigned char __fastcall read_24f210(struct Context *, struct Output);
extern unsigned char __fastcall read_250600(struct Context *, struct Output, struct Output);
extern unsigned char __fastcall read_250630(struct Context *, struct Output, struct Output);
extern unsigned char __fastcall read_2506e0(struct Context *, struct Output, struct Output);
extern unsigned char __fastcall read_250710(struct Context *, struct Output, struct Output);

#define DISPATCH_1(Entry, Read, HandlerOffset, Slot) \
    unsigned char __fastcall Entry(struct Context *self) \
    { \
        struct Word a; \
        struct Output out_a; \
        out_a.word = &a; \
        if (Read(self, out_a)) { \
            struct Handler *handler = *(struct Handler **)((unsigned char *)self + HandlerOffset); \
            Callback1 *methods = *(Callback1 **)handler; \
            methods[Slot](handler, a); \
            return 1; \
        } \
        return 0; \
    }

#define DISPATCH_2(Entry, Read, HandlerOffset, Slot) \
    unsigned char __fastcall Entry(struct Context *self) \
    { \
        struct Word a, b; \
        struct Output out_a, out_b; \
        out_a.word = &a; \
        out_b.word = &b; \
        if (Read(self, out_a, out_b)) { \
            struct Handler *handler = *(struct Handler **)((unsigned char *)self + HandlerOffset); \
            Callback2 *methods = *(Callback2 **)handler; \
            methods[Slot](handler, a, b); \
            return 1; \
        } \
        return 0; \
    }

#define DISPATCH_4(Entry, Read, HandlerOffset, Slot) \
    unsigned char __fastcall Entry(struct Context *self) \
    { \
        struct Word a, b, c, d; \
        struct Output out_a, out_b, out_c, out_d; \
        out_a.word = &a; \
        out_b.word = &b; \
        out_c.word = &c; \
        out_d.word = &d; \
        if (Read(self, out_a, out_b, out_c, out_d)) { \
            struct Handler *handler = *(struct Handler **)((unsigned char *)self + HandlerOffset); \
            Callback4 *methods = *(Callback4 **)handler; \
            methods[Slot](handler, a, b, c, d); \
            return 1; \
        } \
        return 0; \
    }

#define DISPATCH_5(Entry, Read, HandlerOffset, Slot) \
    unsigned char __fastcall Entry(struct Context *self) \
    { \
        struct Word a, b, c, d, e; \
        struct Output out_a, out_b, out_c, out_d, out_e; \
        out_a.word = &a; \
        out_b.word = &b; \
        out_c.word = &c; \
        out_d.word = &d; \
        out_e.word = &e; \
        if (Read(self, out_a, out_b, out_c, out_d, out_e)) { \
            struct Handler *handler = *(struct Handler **)((unsigned char *)self + HandlerOffset); \
            Callback5 *methods = *(Callback5 **)handler; \
            methods[Slot](handler, a, b, c, d, e); \
            return 1; \
        } \
        return 0; \
    }

DISPATCH_2(dispatch_248620, read_244e30, 20, 0)
DISPATCH_2(dispatch_249c10, read_2459f0, 20, 11)
DISPATCH_4(dispatch_249c50, read_245d80, 20, 14)
DISPATCH_5(dispatch_249fb0, read_2468d0, 20, 20)
DISPATCH_4(dispatch_24a180, read_246a80, 20, 22)
DISPATCH_4(dispatch_24a270, read_246e50, 20, 25)
DISPATCH_4(dispatch_24a320, read_2474c0, 20, 28)
DISPATCH_4(dispatch_24a370, read_2476e0, 20, 29)
DISPATCH_4(dispatch_24a710, read_247b70, 20, 32)
DISPATCH_2(dispatch_24aaa0, read_2483a0, 20, 37)
DISPATCH_2(dispatch_24ab20, read_2485a0, 20, 39)
DISPATCH_2(dispatch_24e5b0, read_24d720, 16, 0)
DISPATCH_2(dispatch_24e7e0, read_24dfd0, 16, 11)
DISPATCH_2(dispatch_24e820, read_24e000, 16, 12)
DISPATCH_1(dispatch_24f340, read_24f210, 16, 5)
DISPATCH_2(dispatch_24ffd0, read_250600, 16, 2)
DISPATCH_2(dispatch_250010, read_250630, 16, 3)
DISPATCH_2(dispatch_2500a0, read_2506e0, 16, 6)
DISPATCH_2(dispatch_2500e0, read_250710, 16, 7)
