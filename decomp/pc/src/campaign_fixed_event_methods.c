/* Arguments are forwarded in their original stack slots. */
struct Args4 { unsigned words[1]; };
extern unsigned char __fastcall method_4(unsigned char *, struct Args4);
extern unsigned char __fastcall method_8(unsigned char *, struct Args4, struct Args4);

unsigned char __fastcall forward_4_8_4(unsigned char *self, struct Args4 args)
{
    int displacement = *(const int *)(self - 4);
    unsigned address = (unsigned)self;
    address -= displacement;
    address -= 8;
    return method_4((unsigned char *)address, args);
}

unsigned char __fastcall forward_4_28_4(unsigned char *self, struct Args4 args)
{
    int displacement = *(const int *)(self - 4);
    unsigned address = (unsigned)self;
    address -= displacement;
    address -= 28;
    return method_4((unsigned char *)address, args);
}

unsigned char __fastcall forward_4_48_4(unsigned char *self, struct Args4 args)
{
    int displacement = *(const int *)(self - 4);
    unsigned address = (unsigned)self;
    address -= displacement;
    address -= 48;
    return method_4((unsigned char *)address, args);
}

unsigned char __fastcall forward_12_8_4(unsigned char *self, struct Args4 args)
{
    int displacement = *(const int *)(self - 12);
    unsigned address = (unsigned)self;
    address -= displacement;
    address -= 8;
    return method_4((unsigned char *)address, args);
}

unsigned char __fastcall forward_12_8_8(unsigned char *self, struct Args4 first, struct Args4 second)
{
    int displacement = *(const int *)(self - 12);
    unsigned address = (unsigned)self;
    address -= displacement;
    address -= 8;
    return method_8((unsigned char *)address, first, second);
}

unsigned char __fastcall forward_12_20_8(unsigned char *self, struct Args4 first, struct Args4 second)
{
    int displacement = *(const int *)(self - 12);
    unsigned address = (unsigned)self;
    address -= displacement;
    address -= 20;
    return method_8((unsigned char *)address, first, second);
}

unsigned char __fastcall forward_12_100_8(unsigned char *self, struct Args4 first, struct Args4 second)
{
    int displacement = *(const int *)(self - 12);
    unsigned address = (unsigned)self;
    address -= displacement;
    address -= 100;
    return method_8((unsigned char *)address, first, second);
}
