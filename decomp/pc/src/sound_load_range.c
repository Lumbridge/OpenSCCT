typedef unsigned long Word;
typedef void *File;
File __stdcall SND_fn_hOpenFileReadSnd(const char *path);
Word __stdcall SND_fn_ulReadFileSnd(File file, Word count, void *buffer);
void __stdcall SND_fn_vCloseFileSnd(File file);

Word __stdcall SND_fn_ulSeekFileSnd(File file, long distance, int origin);

int __stdcall SND_fn_bLoadDataInMem(void *destination, Word size, const char *path, long offset)
{
    Word count;
    File file = SND_fn_hOpenFileReadSnd(path);
    if (file != (File)-1) {
        SND_fn_ulSeekFileSnd(file, offset, 0);
        count = SND_fn_ulReadFileSnd(file, size, destination);
        SND_fn_vCloseFileSnd(file);
        if (count == size)
            return 1;
    }
    return 0;
}
