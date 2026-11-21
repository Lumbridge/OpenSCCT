/* x86 addresses and virtual-base displacements are 32 bits in this build. */
struct Flags { unsigned value; };
extern void *__fastcall destroy_adjusted(unsigned char *self, struct Flags flags);

void *__fastcall adjust_12_8(unsigned char *self, struct Flags flags)
{
    int displacement = *(const int *)(self - 12);
    unsigned address = (unsigned)self;
    address -= displacement;
    address -= 8;
    return destroy_adjusted((unsigned char *)address, flags);
}
