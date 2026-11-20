struct Word { unsigned value; };
struct Byte { unsigned char value; };
struct Output { void *address; };
struct Context;
struct Handler;
typedef void (__fastcall *Callback)(struct Handler *, struct Byte, struct Word, struct Word);


extern unsigned char __fastcall read_253cd0(struct Context *, struct Output, struct Output, struct Output);

unsigned char __fastcall dispatch_254110(struct Context *self)
{
    struct Byte first;
    struct Word second, third;
    struct Output a, b, c;
    third.value = 0;
    second.value = 0;
    first.value = 0;
    a.address = &first;
    b.address = &second;
    c.address = &third;
    if (read_253cd0(self, a, b, c)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 24);
        Callback *methods = *(Callback **)handler;
        methods[3](handler, first, second, third);
        return 1;
    }
    return 0;
}

extern unsigned char __fastcall read_254f00(struct Context *, struct Output, struct Output, struct Output);

unsigned char __fastcall dispatch_254ea0(struct Context *self)
{
    struct Byte first;
    struct Word second, third;
    struct Output a, b, c;
    third.value = 0;
    second.value = 0;
    first.value = 39;
    a.address = &first;
    b.address = &second;
    c.address = &third;
    if (read_254f00(self, a, b, c)) {
        struct Handler *handler = *(struct Handler **)((unsigned char *)self + 32);
        Callback *methods = *(Callback **)handler;
        methods[0](handler, first, second, third);
        return 1;
    }
    return 0;
}
