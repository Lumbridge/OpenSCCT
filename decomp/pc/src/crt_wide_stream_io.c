typedef unsigned size_t;
struct Stream {
    char *cursor;
    int left;
    char *base;
    int flags, fd, small_buffer, buffer_size;
    char *temporary_name;
};
enum { Read = 1, Write = 2, Unbuffered = 4, OwnedBuffer = 8, End = 16,
       Error = 32, String = 64, Update = 128, UserBuffer = 256,
       BigBuffer = OwnedBuffer | UserBuffer, AnyBuffer = BigBuffer | Unbuffered };
int _read(int, void *, unsigned);
int _write(int, const void *, unsigned);
int _flush(struct Stream *);
int _filbuf(struct Stream *);
int _flsbuf(int, struct Stream *);
void _getbuf(struct Stream *);

struct DescriptorInfo {
    long handle;
    unsigned char os_flags;
    unsigned char additional[31];
};
extern struct DescriptorInfo *__pioinfo[], __badioinfo;
extern struct Stream _iob[];
void free(void *);
int _isatty(int);
enum { ExplicitBuffer = 0x400, TemporaryBuffer = 0x1000, ControlZ = 0x2000,
       TextMode = 0x80, DescriptorEof = 2 };
#define DESCRIPTOR(fd) ((fd) != -1 ? &__pioinfo[(fd) >> 5][(fd) & 31] : &__badioinfo)

int steam_filwbuf(struct Stream *stream)
{
    if (!(stream->flags & (Read | Write | Update)) || (stream->flags & String))
        return 0xffff;
    if (stream->flags & Write) {
        stream->flags |= Error;
        return 0xffff;
    }
    stream->flags |= Read;
    if (!(stream->flags & AnyBuffer))
        _getbuf(stream);
    else
        stream->cursor = stream->base;
    stream->left = _read(stream->fd, stream->base, stream->buffer_size);
    if (stream->left == 0 || stream->left == 1 || stream->left == -1) {
        stream->flags |= stream->left == 0 ? End : Error;
        stream->left = 0;
        return 0xffff;
    }
    if (!(stream->flags & (Write | Update)) &&
        (DESCRIPTOR(stream->fd)->os_flags & (TextMode | DescriptorEof)) == (TextMode | DescriptorEof))
        stream->flags |= ControlZ;
    if (stream->buffer_size == 512 && (stream->flags & OwnedBuffer) &&
        !(stream->flags & ExplicitBuffer))
        stream->buffer_size = 4096;
    stream->left -= 2;
    {
        short *cursor = (short *)stream->cursor;
        int value = *cursor++ & 0xffff;
        stream->cursor = (char *)cursor;
        return value;
    }
}

typedef unsigned short WideChar;
enum { WideEof = 0xffff };
int wctomb(char *, WideChar);
int _flswbuf(int, struct Stream *);
int *_errno(void);

#define IS_TEXT(stream) (!((stream)->flags & String) && (DESCRIPTOR((stream)->fd)->os_flags & TextMode))
#define PUT_BYTE(byte, stream) (--(stream)->left >= 0 ? \
    (*(stream)->cursor = (byte), (unsigned char)*(stream)->cursor++) : _flsbuf(byte, stream))

WideChar steam_putwc_lk(WideChar c, struct Stream *stream)
{
    if (IS_TEXT(stream)) {
        char bytes[5];
        int i, n = wctomb(bytes, c);
        if (n == -1) { *_errno() = 42; return WideEof; }
        for (i = 0; i < n; ++i)
            if (PUT_BYTE(bytes[i], stream) == -1) return WideEof;
        return c;
    }
    if ((stream->left -= sizeof(WideChar)) >= 0) {
        *(WideChar *)stream->cursor = c;
        stream->cursor += sizeof(WideChar);
        return c;
    }
    return (WideChar)_flswbuf(c, stream);
}

WideChar steam_ungetwc_lk(WideChar c, struct Stream *stream)
{
    if (c == WideEof || (!(stream->flags & Read) && (!(stream->flags & Update) || (stream->flags & Write))))
        return WideEof;
    if (!stream->base) _getbuf(stream);
    if (IS_TEXT(stream)) {
        char bytes[5];
        int i, n = wctomb(bytes, c);
        if (n == -1) { *_errno() = 42; return WideEof; }
        if (stream->cursor < stream->base + n) {
            if (stream->left || n > stream->buffer_size) return WideEof;
            stream->cursor = stream->base + n;
        }
        for (i = n - 1; i >= 0; --i)
            *--stream->cursor = bytes[i];
        stream->left += n;
    } else {
        if (stream->cursor < stream->base + sizeof(WideChar)) {
            if (stream->left || sizeof(WideChar) > (unsigned)stream->buffer_size) return WideEof;
            stream->cursor = stream->base + sizeof(WideChar);
        }
        stream->cursor -= sizeof(WideChar);
        if (stream->flags & String) {
            if (*(WideChar *)stream->cursor != c) {
                stream->cursor += sizeof(WideChar);
                return WideEof;
            }
        } else {
            *(WideChar *)stream->cursor = c;
        }
        stream->left += sizeof(WideChar);
    }
    stream->flags &= ~End;
    stream->flags |= Read;
    return c;
}
long _lseek(int, long, int);
int steam_flsbuf(int c, struct Stream *stream)
{
    int fd = stream->fd, count, written;
    if (!(stream->flags & (Write | Update)) || (stream->flags & String)) {
        stream->flags |= Error;
        return -1;
    }
    if (stream->flags & Read) {
        stream->left = 0;
        if (!(stream->flags & End)) {
            stream->flags |= Error;
            return -1;
        }
        stream->cursor = stream->base;
        stream->flags &= ~Read;
    }
    stream->flags |= Write;
    stream->flags &= ~End;
    stream->left = 0;
    written = 0;
    if (!(stream->flags & AnyBuffer) &&
        ((stream != &_iob[1] && stream != &_iob[2]) || !_isatty(fd))) _getbuf(stream);
    if (stream->flags & BigBuffer) {
        count = (int)(stream->cursor - stream->base);
        stream->cursor = stream->base + 1;
        stream->left = stream->buffer_size - 1;
        if (count > 0)
            written = _write(fd, stream->base, count);
        else if (DESCRIPTOR(fd)->os_flags & 0x20)
            _lseek(fd, 0, 2);
        *stream->base = (char)c;
    } else {
        count = 1;
        written = _write(fd, &c, 1);
    }
    if (written != count) {
        stream->flags |= Error;
        return -1;
    }
    return c & 0xff;
}

int _commit(int fd);
int _close(int fd);
void _freebuf(struct Stream *);

int steam_fflush_lk(struct Stream *stream)
{
    if (_flush(stream)) return -1;
    if (stream->flags & 0x4000)
        return _commit(stream->fd) ? -1 : 0;
    return 0;
}

int steam_fclose_lk(struct Stream *stream)
{
    int result = -1;
    if (stream->flags & (Read | Write | Update)) {
        result = _flush(stream);
        _freebuf(stream);
        if (_close(stream->fd) < 0)
            result = -1;
        else if (stream->temporary_name) {
            free(stream->temporary_name);
            stream->temporary_name = 0;
        }
    }
    stream->flags = 0;
    return result;
}
