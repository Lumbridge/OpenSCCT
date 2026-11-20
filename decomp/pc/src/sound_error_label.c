typedef struct ErrorEntry {
    unsigned short code;
    unsigned short padding;
    const char *label;
} ErrorEntry;
extern const ErrorEntry SOUND_ERRORS[];
#define UNKNOWN_ERROR ((const char *)0x11171b9c)

const char *__cdecl sound_error_label(int requested)
{
    int index = 0;
    unsigned short code = SOUND_ERRORS[0].code;
    while (code != requested && code != 0xffff) {
        ++index;
        code = SOUND_ERRORS[index].code;
    }
    return SOUND_ERRORS[index].code == 0xffff ? UNKNOWN_ERROR : SOUND_ERRORS[index].label;
}
