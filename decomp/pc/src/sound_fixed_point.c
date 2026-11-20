/* The operands are signed 16.16 values. The fast path discards eight
   fractional bits from each before forming the low 32 bits of the product. */
long __stdcall SND_fn_rMulRealRealQuickSnd(long left, long right)
{
    return (unsigned long)(left >> 8) * (unsigned long)(right >> 8);
}
