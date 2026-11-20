typedef unsigned short WideChar;
struct Stream {
    char *cursor;
    int left;
    char *base;
    int flags, fd, small_buffer, buffer_size;
    char *temporary_name;
};
extern int _commode, _cflush;
int _sopen(const char *, int, int, int);
int _wsopen(const WideChar *, int, int, int);
enum OpenMode {
    ReadOnly = 0, WriteOnly = 1, ReadWrite = 2, Append = 8,
    Random = 0x10, Sequential = 0x20, DeleteOnClose = 0x40, Create = 0x100,
    Truncate = 0x200, ShortLived = 0x1000, Text = 0x4000, Binary = 0x8000
};
enum StreamMode { Read = 1, Write = 2, Update = 0x80, Commit = 0x4000 };

#define OPEN_STREAM(function, character, open_file) \
struct Stream *function(const character *path, const character *mode, int sharing, struct Stream *stream) \
{ \
    int flags, stream_flags = _commode, commit_seen = 0, hint_seen = 0, parsing, fd; \
    switch (*mode) { \
    case 'r': flags = ReadOnly; stream_flags |= Read; break; \
    case 'w': flags = WriteOnly | Create | Truncate; stream_flags |= Write; break; \
    case 'a': flags = WriteOnly | Create | Append; stream_flags |= Write; break; \
    default: return 0; \
    } \
    parsing = 1; \
    while (*++mode && parsing) { \
        switch (*mode) { \
        case '+': \
            if (flags & ReadWrite) parsing = 0; \
            else { flags = (flags & ~WriteOnly) | ReadWrite; stream_flags = (stream_flags & ~(Read|Write)) | Update; } \
            break; \
        case 'b': if (flags & (Binary | Text)) parsing = 0; else { flags |= Binary; } break; \
        case 't': if (flags & (Binary | Text)) parsing = 0; else { flags |= Text; } break; \
        case 'c': if (commit_seen) parsing = 0; else { commit_seen = 1; stream_flags |= Commit; } break; \
        case 'n': if (commit_seen) parsing = 0; else { commit_seen = 1; stream_flags &= ~Commit; } break; \
        case 'S': if (hint_seen) parsing = 0; else { hint_seen = 1; flags |= Sequential; } break; \
        case 'R': if (hint_seen) parsing = 0; else { hint_seen = 1; flags |= Random; } break; \
        case 'T': if (flags & ShortLived) parsing = 0; else flags |= ShortLived; break; \
        case 'D': if (flags & DeleteOnClose) parsing = 0; else flags |= DeleteOnClose; break; \
        default: parsing = 0; \
        } \
    } \
    fd = open_file(path, flags, sharing, 0x1a4); \
    if (fd < 0) return 0; \
    ++_cflush; stream->flags = stream_flags; stream->left = 0; \
    stream->cursor = 0; stream->base = 0; stream->temporary_name = 0; stream->fd = fd; \
    return stream; \
}
OPEN_STREAM(steam_openfile, char, _sopen)
OPEN_STREAM(steam_wopenfile, WideChar, _wsopen)
