struct Context;
struct Handler;
struct Output { void *address; };
struct Word { unsigned value; };

typedef void (__fastcall *Callback_249620)(struct Handler *, struct Word, struct Word, struct Output, struct Word, struct Word);
extern unsigned char __fastcall read_245bb0(struct Context *, struct Output, struct Output, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_249620(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    unsigned char value3[36];
    struct Word value4;
    struct Word value5;
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    struct Output out_value4;
    struct Output out_value5;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    out_value4.address = &value4;
    out_value5.address = &value5;
    if (read_245bb0(self, out_value1, out_value2, out_value3, out_value4, out_value5)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 20);
        Callback_249620 *methods = *(Callback_249620 **)handler;
        methods[3](handler, value1, value2, out_value3, value4, value5);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_249ca0)(struct Handler *, struct Word, struct Word, struct Output, struct Word, struct Word);
extern unsigned char __fastcall read_246170(struct Context *, struct Output, struct Output, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_249ca0(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    unsigned char value3[16];
    struct Word value4;
    struct Word value5;
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    struct Output out_value4;
    struct Output out_value5;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    out_value4.address = &value4;
    out_value5.address = &value5;
    if (read_246170(self, out_value1, out_value2, out_value3, out_value4, out_value5)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 20);
        Callback_249ca0 *methods = *(Callback_249ca0 **)handler;
        methods[15](handler, value1, value2, out_value3, value4, value5);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_249f00)(struct Handler *, struct Word, struct Word, struct Output, struct Word, struct Word);
extern unsigned char __fastcall read_246370(struct Context *, struct Output, struct Output, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_249f00(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    unsigned char value3[16];
    struct Word value4;
    struct Word value5;
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    struct Output out_value4;
    struct Output out_value5;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    out_value4.address = &value4;
    out_value5.address = &value5;
    if (read_246370(self, out_value1, out_value2, out_value3, out_value4, out_value5)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 20);
        Callback_249f00 *methods = *(Callback_249f00 **)handler;
        methods[19](handler, value1, value2, out_value3, value4, value5);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24a770)(struct Handler *, struct Word, struct Word, struct Word, struct Word, struct Output);
extern unsigned char __fastcall read_247d90(struct Context *, struct Output, struct Output, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24a770(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    struct Word value3;
    struct Word value4;
    unsigned char value5[16];
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    struct Output out_value4;
    struct Output out_value5;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    out_value4.address = &value4;
    out_value5.address = &value5;
    if (read_247d90(self, out_value1, out_value2, out_value3, out_value4, out_value5)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 20);
        Callback_24a770 *methods = *(Callback_24a770 **)handler;
        methods[33](handler, value1, value2, value3, value4, out_value5);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24a7d0)(struct Handler *, struct Word, struct Word, struct Word, struct Word, struct Output);
extern unsigned char __fastcall read_247f90(struct Context *, struct Output, struct Output, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24a7d0(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    struct Word value3;
    struct Word value4;
    unsigned char value5[16];
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    struct Output out_value4;
    struct Output out_value5;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    out_value4.address = &value4;
    out_value5.address = &value5;
    if (read_247f90(self, out_value1, out_value2, out_value3, out_value4, out_value5)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 20);
        Callback_24a7d0 *methods = *(Callback_24a7d0 **)handler;
        methods[34](handler, value1, value2, value3, value4, out_value5);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24aae0)(struct Handler *, struct Word);
extern unsigned char __fastcall read_248570(struct Context *, struct Output);
unsigned char __fastcall dispatch_24aae0(struct Context *self)
{
    struct Word value1 = { 0 };
    struct Output out_value1;
    out_value1.address = &value1;
    if (read_248570(self, out_value1)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 20);
        Callback_24aae0 *methods = *(Callback_24aae0 **)handler;
        methods[38](handler, value1);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24e8d0)(struct Handler *, struct Word, struct Word, struct Output, struct Word, struct Output);
extern unsigned char __fastcall read_24e160(struct Context *, struct Output, struct Output, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24e8d0(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    unsigned char value3[16];
    struct Word value4;
    unsigned char value5[132];
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    struct Output out_value4;
    struct Output out_value5;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    out_value4.address = &value4;
    out_value5.address = &value5;
    if (read_24e160(self, out_value1, out_value2, out_value3, out_value4, out_value5)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24e8d0 *methods = *(Callback_24e8d0 **)handler;
        methods[14](handler, value1, value2, out_value3, value4, out_value5);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24e940)(struct Handler *, struct Word, struct Word, struct Output, struct Output, struct Output);
extern unsigned char __fastcall read_24e380(struct Context *, struct Output, struct Output, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24e940(struct Context *self)
{
    struct Word value1;
    struct Word value2;
    unsigned char value3[16];
    unsigned char value4[36];
    unsigned char value5[1024];
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    struct Output out_value4;
    struct Output out_value5;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    out_value4.address = &value4;
    out_value5.address = &value5;
    if (read_24e380(self, out_value1, out_value2, out_value3, out_value4, out_value5)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24e940 *methods = *(Callback_24e940 **)handler;
        methods[15](handler, value1, value2, out_value3, out_value4, out_value5);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24f2c0)(struct Handler *, struct Word, struct Output);
extern unsigned char __fastcall read_24edc0(struct Context *, struct Output, struct Output);
unsigned char __fastcall dispatch_24f2c0(struct Context *self)
{
    struct Word value1;
    unsigned char value2[16];
    struct Output out_value1;
    struct Output out_value2;
    out_value1.address = &value1;
    out_value2.address = &value2;
    if (read_24edc0(self, out_value1, out_value2)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24f2c0 *methods = *(Callback_24f2c0 **)handler;
        methods[3](handler, value1, out_value2);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24f300)(struct Handler *, struct Word, struct Output);
extern unsigned char __fastcall read_24eff0(struct Context *, struct Output, struct Output);
unsigned char __fastcall dispatch_24f300(struct Context *self)
{
    struct Word value1;
    unsigned char value2[16];
    struct Output out_value1;
    struct Output out_value2;
    out_value1.address = &value1;
    out_value2.address = &value2;
    if (read_24eff0(self, out_value1, out_value2)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24f300 *methods = *(Callback_24f300 **)handler;
        methods[4](handler, value1, out_value2);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24f370)(struct Handler *, struct Output);
extern unsigned char __fastcall read_24f280(struct Context *, struct Output);
unsigned char __fastcall dispatch_24f370(struct Context *self)
{
    unsigned char value1[16];
    struct Output out_value1;
    out_value1.address = &value1;
    if (read_24f280(self, out_value1)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24f370 *methods = *(Callback_24f370 **)handler;
        methods[6](handler, out_value1);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_24ff70)(struct Handler *, struct Word, struct Output, struct Word, struct Word);
extern unsigned char __fastcall read_2503e0(struct Context *, struct Output, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_24ff70(struct Context *self)
{
    struct Word value1;
    unsigned char value2[132];
    struct Word value3;
    struct Word value4;
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    struct Output out_value4;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    out_value4.address = &value4;
    if (read_2503e0(self, out_value1, out_value2, out_value3, out_value4)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_24ff70 *methods = *(Callback_24ff70 **)handler;
        methods[1](handler, value1, out_value2, value3, value4);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_250050)(struct Handler *, struct Word, struct Output);
extern unsigned char __fastcall read_250660(struct Context *, struct Output, struct Output);
unsigned char __fastcall dispatch_250050(struct Context *self)
{
    struct Word value1;
    unsigned char value2[1028];
    struct Output out_value1;
    struct Output out_value2;
    out_value1.address = &value1;
    out_value2.address = &value2;
    if (read_250660(self, out_value1, out_value2)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_250050 *methods = *(Callback_250050 **)handler;
        methods[4](handler, value1, out_value2);
        return 1;
    }
    return 0;
}

typedef void (__fastcall *Callback_250120)(struct Handler *, struct Word, struct Output, struct Output, struct Word);
extern unsigned char __fastcall read_250740(struct Context *, struct Output, struct Output, struct Output, struct Output);
unsigned char __fastcall dispatch_250120(struct Context *self)
{
    struct Word value1;
    unsigned char value2[516];
    unsigned char value3[516];
    struct Word value4;
    struct Output out_value1;
    struct Output out_value2;
    struct Output out_value3;
    struct Output out_value4;
    out_value1.address = &value1;
    out_value2.address = &value2;
    out_value3.address = &value3;
    out_value4.address = &value4;
    if (read_250740(self, out_value1, out_value2, out_value3, out_value4)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 16);
        Callback_250120 *methods = *(Callback_250120 **)handler;
        methods[8](handler, value1, out_value2, out_value3, value4);
        return 1;
    }
    return 0;
}
