/* The ranges reflect the decimal alphabets recognized by this Windows CRT. */
int __cdecl steam_wchartodigit(unsigned short ch)
{
    unsigned zero;
    if (ch < '0')
        return -1;
    if (ch < '0' + 10)
        return ch - '0';
    zero = 0xff10; /* Fullwidth */
    if (ch < (unsigned short)zero) {
#define DIGIT_BLOCK(base) \
        zero = (base); \
        if (ch < (unsigned short)zero) return -1; \
        if (ch < (base) + 10) return ch - zero;
        DIGIT_BLOCK(0x0660) /* Arabic-Indic */
        DIGIT_BLOCK(0x06f0) /* Extended Arabic-Indic */
        DIGIT_BLOCK(0x0966) /* Devanagari */
        DIGIT_BLOCK(0x09e6) /* Bengali */
        DIGIT_BLOCK(0x0a66) /* Gurmukhi */
        DIGIT_BLOCK(0x0ae6) /* Gujarati */
        DIGIT_BLOCK(0x0b66) /* Oriya */
        DIGIT_BLOCK(0x0c66) /* Telugu */
        DIGIT_BLOCK(0x0ce6) /* Kannada */
        DIGIT_BLOCK(0x0d66) /* Malayalam */
        DIGIT_BLOCK(0x0e50) /* Thai */
        DIGIT_BLOCK(0x0ed0) /* Lao */
        DIGIT_BLOCK(0x0f20) /* Tibetan */
        DIGIT_BLOCK(0x1040) /* Myanmar */
        DIGIT_BLOCK(0x17e0) /* Khmer */
        DIGIT_BLOCK(0x1810) /* Mongolian */
#undef DIGIT_BLOCK
    } else if (ch < 0xff1a) {
        return ch - zero;
    }
    return -1;
}
