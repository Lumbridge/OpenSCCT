struct Context;
struct Handler;
struct Output { void *address; };
struct Word { unsigned value; };

typedef void (__fastcall *Callback_2495d0)(struct Handler *, struct Word, struct Word, struct Word);
extern unsigned char __fastcall read_244fd0(struct Context *, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_2495d0(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    struct Word value3;
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    if (read_244fd0(self, out_value1, out_value2, out_value3)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 20);
        Callback_2495d0 *methods = *(Callback_2495d0 **)handler;
        methods[10](handler, value1, value2, value3);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_249f60)(struct Handler *, struct Word, struct Word, struct Output);
extern unsigned char __fastcall read_246570(struct Context *, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_249f60(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    unsigned char value3[1028];
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    if (read_246570(self, out_value1, out_value2, out_value3)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 20);
        Callback_249f60 *methods = *(Callback_249f60 **)handler;
        methods[18](handler, value1, value2, out_value3);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24a1d0)(struct Handler *, struct Word, struct Word, struct Word);
extern unsigned char __fastcall read_246720(struct Context *, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24a1d0(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    struct Word value3;
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    if (read_246720(self, out_value1, out_value2, out_value3)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 20);
        Callback_24a1d0 *methods = *(Callback_24a1d0 **)handler;
        methods[23](handler, value1, value2, value3);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24a220)(struct Handler *, struct Word, struct Word, struct Word);
extern unsigned char __fastcall read_246ca0(struct Context *, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24a220(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    struct Word value3 = { 0 };
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    if (read_246ca0(self, out_value1, out_value2, out_value3)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 20);
        Callback_24a220 *methods = *(Callback_24a220 **)handler;
        methods[24](handler, value1, value2, value3);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24a2c0)(struct Handler *, struct Word, struct Word, struct Word);
extern unsigned char __fastcall read_247070(struct Context *, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24a2c0(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    struct Word value3;
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    if (read_247070(self, out_value1, out_value2, out_value3)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 20);
        Callback_24a2c0 *methods = *(Callback_24a2c0 **)handler;
        methods[26](handler, value1, value2, value3);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24e5f0)(struct Handler *, struct Word, struct Word, struct Output);
extern unsigned char __fastcall read_24d750(struct Context *, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24e5f0(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    unsigned char value3[16];
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    if (read_24d750(self, out_value1, out_value2, out_value3)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24e5f0 *methods = *(Callback_24e5f0 **)handler;
        methods[1](handler, value1, value2, out_value3);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24e640)(struct Handler *, struct Word, struct Word, struct Output);
extern unsigned char __fastcall read_24d7c0(struct Context *, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24e640(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    unsigned char value3[16];
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    if (read_24d7c0(self, out_value1, out_value2, out_value3)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24e640 *methods = *(Callback_24e640 **)handler;
        methods[2](handler, value1, value2, out_value3);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24e6e0)(struct Handler *, struct Word, struct Word, struct Output);
extern unsigned char __fastcall read_24d900(struct Context *, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24e6e0(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    unsigned char value3[16];
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    if (read_24d900(self, out_value1, out_value2, out_value3)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24e6e0 *methods = *(Callback_24e6e0 **)handler;
        methods[8](handler, value1, value2, out_value3);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24e790)(struct Handler *, struct Word, struct Word, struct Output);
extern unsigned char __fastcall read_24dd90(struct Context *, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24e790(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    unsigned char value3[16];
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    if (read_24dd90(self, out_value1, out_value2, out_value3)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24e790 *methods = *(Callback_24e790 **)handler;
        methods[10](handler, value1, value2, out_value3);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24e860)(struct Handler *, struct Output, struct Word, struct Output, struct Word, struct Word, struct Output);
extern unsigned char __fastcall read_24e030(struct Context *, struct Output, struct Output, struct Output, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24e860(struct Context *self)
{
    unsigned char value1[16];
    struct Word value2;
    unsigned char value3[36];
    struct Word value4;
    struct Word value5;
    unsigned char value6[132];
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    struct Output out_value4;
    struct Output out_value5;
    struct Output out_value6;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    out_value4.address = &value4;
    out_value5.address = &value5;
    out_value6.address = &value6;
    if (read_24e030(self, out_value1, out_value2, out_value3, out_value4, out_value5, out_value6)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24e860 *methods = *(Callback_24e860 **)handler;
        methods[13](handler, out_value1, value2, out_value3, value4, value5, out_value6);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24e690)(struct Handler *, struct Output, struct Output, struct Output);
extern unsigned char __fastcall read_24d830(struct Context *, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24e690(struct Context *self)
{
    struct {
        unsigned char value3[16];
        unsigned char value1[16];
        unsigned char value2[1028];
    } decoded;
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    out_value1.address = &decoded.value1;
    out_value2.address = &decoded.value2;
    out_value3.address = &decoded.value3;
    if (read_24d830(self, out_value1, out_value2, out_value3)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24e690 *methods = *(Callback_24e690 **)handler;
        methods[7](handler, out_value1, out_value2, out_value3);
        return 1;
    }
    return 0;
}
