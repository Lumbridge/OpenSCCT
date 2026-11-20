typedef unsigned long Word;
typedef void *File;
void __stdcall SND_fn_vDisplayError(int code, const char *context);

typedef Word (__stdcall *SetFilePointerFn)(File file, long distance, long *high, Word method);
#define SET_FILE_POINTER (*(SetFilePointerFn const *)0x110b73c4)

Word __stdcall SND_fn_ulSeekFileSnd(File file, long distance, int origin)
{
    Word method;
    Word result;
    switch (origin) {
    default:
        SND_fn_vDisplayError(13, (const char *)0x11178220);
    case 0: method = 0; break;
    case 1: method = 1; break;
    case 2: method = 2; break;
    }
    result = SET_FILE_POINTER(file, distance, 0, method);
    if (result == (Word)-1 || result == 131)
        return (Word)-1;
    return result;
}
