unsigned long steam_byteswap_ulong(unsigned long value)
{
    return ((value & 0xfful) << 24) + ((value & 0xff00ul) << 8) +
           ((value & 0xff0000ul) >> 8) + ((value & 0xff000000ul) >> 24);
}
