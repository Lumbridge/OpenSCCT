/* The named audio directory exports reference the path buffers at these image VAs. */
#define SND_SOFT_PATH ((char *)0x1132c470)
#define SND_CURRENT_LANG_PATH ((char *)0x1132c570)

char *__stdcall SND_fn_szGetSoftDirectory(void)
{
    return SND_SOFT_PATH;
}

char *__stdcall SND_fn_szGetCurrentLangDirectory(void)
{
    return SND_CURRENT_LANG_PATH;
}

char __stdcall SND_fn_cGetDirectorySeparator(void)
{
    return '\\';
}
