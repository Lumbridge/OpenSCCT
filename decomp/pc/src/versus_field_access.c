/* Offsets are relative to the receiver. Its original type is unknown. */
struct Argument { unsigned value; };

unsigned __fastcall read_word_004c50(const unsigned char *self)
{
    return *(const unsigned *)(self + 420);
}

unsigned __fastcall read_word_0a51c0(const unsigned char *self)
{
    return *(const unsigned *)(self + 20);
}

unsigned __fastcall read_word_0f3d80(const unsigned char *self)
{
    return *(const unsigned *)(self + 12);
}

unsigned __fastcall read_word_112a10(const unsigned char *self)
{
    return *(const unsigned *)(self + 8);
}

void __fastcall write_word_1710c0(unsigned char *self, struct Argument arg)
{
    *(unsigned *)(self + 32) = arg.value;
}

void __fastcall write_word_239990(unsigned char *self, struct Argument arg)
{
    *(unsigned *)(self + 200) = arg.value;
}

void __fastcall write_word_2399a0(unsigned char *self, struct Argument arg)
{
    *(unsigned *)(self + 204) = arg.value;
}

void __fastcall write_word_2399b0(unsigned char *self, struct Argument arg)
{
    *(unsigned *)(self + 208) = arg.value;
}

void __fastcall write_word_2399c0(unsigned char *self, struct Argument arg)
{
    *(unsigned *)(self + 212) = arg.value;
}
