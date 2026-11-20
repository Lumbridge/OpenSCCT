extern const char *ui_names[][30];
extern int ui_name_counts[];
__declspec(dllimport) int __cdecl _stricmp(const char *, const char *);

const char *__cdecl ui_name(int category, int index)
{
    return ui_names[category][index];
}

int __cdecl find_ui_name(int category, const char *name)
{
    int i;
    for (i = 0; i < ui_name_counts[category]; ++i) {
        if (_stricmp(name, ui_names[category][i]) == 0)
            return i;
    }
    return -1;
}

unsigned char __cdecl is_ui_type(unsigned type)
{
    switch (type) {
    case 0:
    case 1:
    case 2:
    case 3:
    case 7:
    case 8:
    case 10:
        return 0;
    case 4:
    case 6:
    case 9:
        return 1;
    default:
        return 0;
    }
}
