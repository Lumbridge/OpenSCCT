struct Receiver;
struct Word { unsigned value; };
/* The receiver is in ECX and the command is on the stack. EDX carries
   the displacement at this virtual call site. */
typedef void (__fastcall *Action)(struct Receiver *, unsigned, unsigned);
struct Receiver { Action *methods; };
void __fastcall dispatch_code(unsigned char *self, unsigned unused, unsigned code)
{
    if (code == 47) {
        const int *offsets = *(const int *const *)(self - 28);
        struct Receiver *receiver = (struct Receiver *)(self + offsets[1] - 28);
        receiver->methods[15](receiver, offsets[1], 0);
    }
}

extern void __fastcall code_method(unsigned char *, struct Word);

void __fastcall adjust_command_20_48(unsigned char *self, struct Word code)
{
    int displacement = *(const int *)(self - 20);
    unsigned address = (unsigned)self;
    address -= displacement;
    address -= 48;
    code_method((unsigned char *)address, code);
}

void __fastcall adjust_command_20_100(unsigned char *self, struct Word code)
{
    int displacement = *(const int *)(self - 20);
    unsigned address = (unsigned)self;
    address -= displacement;
    address -= 100;
    code_method((unsigned char *)address, code);
}

void __fastcall adjust_command_20_112(unsigned char *self, struct Word code)
{
    int displacement = *(const int *)(self - 20);
    unsigned address = (unsigned)self;
    address -= displacement;
    address -= 112;
    code_method((unsigned char *)address, code);
}
