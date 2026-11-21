/* The argument word is forwarded unchanged. Its interpretation belongs to
   the destination method. Both method signatures return their result in AL. */
struct Argument { unsigned word; };
extern unsigned char __fastcall method_without_argument(unsigned char *);
extern unsigned char __fastcall method_with_argument(unsigned char *, struct Argument);

unsigned char __fastcall adjust_no_arg(unsigned char *self)
{
    return method_without_argument(self - ((const int *)self)[-1]);
}
unsigned char __fastcall adjust_one_arg(unsigned char *self, struct Argument arg)
{
    return method_with_argument(self - ((const int *)self)[-1], arg);
}

extern void __fastcall finish_operation(unsigned char *part);
static __forceinline unsigned char *state(unsigned char *self)
{
    const int *offsets = *(const int *const *)(self - 44);
    return self + offsets[1] + 16;
}
unsigned char __fastcall finish_if_active(unsigned char *self)
{
    if (*state(self)) {
        finish_operation(self - 8);
        *state(self) = 0;
    }
    return 1;
}
