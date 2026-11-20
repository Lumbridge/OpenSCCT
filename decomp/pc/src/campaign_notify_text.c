typedef unsigned short WChar;
struct NameEntry { unsigned header[3]; unsigned char text[1]; };
struct Name { int index; };
struct Notify { float time; struct Name name; unsigned remaining[2]; };
struct Clip { unsigned char reserved[0x1c]; struct Notify *notifies; };
extern struct NameEntry **name_entries;
extern int next_buffer;
extern WChar name_buffers[1024][100];

__declspec(noinline) WChar *__cdecl widen_name(WChar *out, const unsigned char *text)
{
    int i;
    if (!text)
        return 0;
    for (i = 0; text[i]; ++i)
        out[i] = text[i];
    out[i] = 0;
    return out;
}

static __forceinline WChar *render_name(const struct Name *name)
{
    WChar *out = name_buffers[next_buffer];
    next_buffer = (next_buffer + 1) % 1024;
    return widen_name(out, name_entries[name->index]->text);
}

WChar *__fastcall notify_text(void *self, void *unused, const struct Clip *clip, int index)
{
    if (clip)
        return render_name(&clip->notifies[index].name);
    return 0;
}
