struct OneWord { unsigned word; };
struct TwoWords { unsigned words[2]; };

unsigned char __fastcall mark_one(unsigned char *self, struct OneWord args)
{
    const int *offsets = *(const int *const *)(self - 44);
    unsigned char *owner = *(unsigned char **)(self + offsets[1] - 24);
    owner[20] = 1;
    return 1;
}

extern unsigned char __fastcall target_mark_one(unsigned char *self, struct OneWord args);
unsigned char __fastcall adjust_mark_one(unsigned char *self, struct OneWord args)
{
    return target_mark_one(self - ((const int *)self)[-1], args);
}

unsigned char __fastcall accept(unsigned char *self, struct OneWord args)
{
    return 1;
}

extern unsigned char __fastcall target_accept(unsigned char *self, struct OneWord args);
unsigned char __fastcall adjust_accept(unsigned char *self, struct OneWord args)
{
    return target_accept(self - ((const int *)self)[-1], args);
}

unsigned char __fastcall mark_two(unsigned char *self, struct TwoWords args)
{
    const int *offsets = *(const int *const *)(self - 64);
    unsigned char *owner = *(unsigned char **)(self + offsets[1] - 44);
    owner[20] = 1;
    return 1;
}

extern unsigned char __fastcall target_mark_two(unsigned char *self, struct TwoWords args);
unsigned char __fastcall adjust_mark_two(unsigned char *self, struct TwoWords args)
{
    return target_mark_two(self - ((const int *)self)[-1], args);
}

unsigned char __fastcall read_flag(unsigned char *self)
{
    return self[-8];
}

extern unsigned char __fastcall target_read_flag(unsigned char *self);
unsigned char __fastcall adjust_read_flag(unsigned char *self)
{
    return target_read_flag(self - ((const int *)self)[-1]);
}
