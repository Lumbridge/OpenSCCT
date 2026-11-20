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
void *memcpy(void *, const void *, size_t);
int _read(int, void *, unsigned);
int _write(int, const void *, unsigned);
int _flush(struct Stream *);
int _filbuf(struct Stream *);
int _flsbuf(int, struct Stream *);
void _getbuf(struct Stream *);

size_t steam_fread(void *buffer, size_t size, size_t count, struct Stream *stream)
{
    char *cursor = buffer;
    size_t total = size * count, remaining = total, block_size, chunk;
    int value;
    if (!remaining) return 0;
    block_size = (stream->flags & AnyBuffer) ? stream->buffer_size : 4096;
    while (remaining) {
        if ((stream->flags & AnyBuffer) && stream->left) {
            chunk = remaining < (size_t)stream->left ? remaining : (size_t)stream->left;
            memcpy(cursor, stream->cursor, chunk);
            remaining -= chunk;
            stream->left -= (int)chunk;
            stream->cursor += chunk;
            cursor += chunk;
        } else if (remaining >= block_size) {
            chunk = block_size ? remaining - remaining % block_size : remaining;
            value = _read(stream->fd, cursor, (unsigned)chunk);
            if (value == 0) {
                stream->flags |= End;
                return (total - remaining) / size;
            }
            if (value == -1) {
                stream->flags |= Error;
                return (total - remaining) / size;
            }
            remaining -= value;
            cursor += value;
        } else {
            value = _filbuf(stream);
            if (value == -1)
                return (total - remaining) / size;
            *cursor++ = (char)value;
            --remaining;
            block_size = stream->buffer_size;
        }
    }
    return count;
}

size_t steam_fwrite(const void *buffer, size_t size, size_t count, struct Stream *stream)
{
    const char *cursor = buffer;
    size_t total = size * count, remaining = total, block_size, chunk;
    int written;
    if (!remaining) return 0;
    block_size = (stream->flags & AnyBuffer) ? stream->buffer_size : 4096;
    while (remaining) {
        if ((stream->flags & BigBuffer) && stream->left) {
            chunk = remaining < (size_t)stream->left ? remaining : (size_t)stream->left;
            memcpy(stream->cursor, cursor, chunk);
            stream->left -= (int)chunk;
            stream->cursor += chunk;
            remaining -= chunk;
            cursor += chunk;
        } else if (remaining >= block_size) {
            if ((stream->flags & BigBuffer) && _flush(stream))
                return (total - remaining) / size;
            chunk = block_size ? remaining - remaining % block_size : remaining;
            written = _write(stream->fd, cursor, (unsigned)chunk);
            if (written == -1) {
                stream->flags |= Error;
                return (total - remaining) / size;
            }
            cursor += written;
            remaining -= written;
            if ((size_t)written < chunk) {
                stream->flags |= Error;
                return (total - remaining) / size;
            }
        } else {
            if (_flsbuf(*cursor, stream) == -1)
                return (total - remaining) / size;
            ++cursor;
            --remaining;
            block_size = stream->buffer_size > 0 ? stream->buffer_size : 1;
        }
    }
    return count;
}

int steam_ungetc(int value, struct Stream *stream)
{
    if (value == -1 || (!(stream->flags & Read) &&
        (!(stream->flags & Update) || (stream->flags & Write)))) return -1;
    if (!stream->base)
        _getbuf(stream);
    if (stream->cursor == stream->base) {
        if (stream->left) return -1;
        ++stream->cursor;
    }
    --stream->cursor;
    if (stream->flags & String) {
        if ((unsigned char)*stream->cursor != (unsigned char)value) {
            ++stream->cursor;
            return -1;
        }
    } else {
        *stream->cursor = (char)value;
    }
    ++stream->left;
    stream->flags &= ~End;
    stream->flags |= Read;
    return value & 0xff;
}

int steam_flush(struct Stream *stream)
{
    int result = 0;
    if ((stream->flags & (Read|Write)) == Write && (stream->flags & BigBuffer)) {
        int length = (int)(stream->cursor - stream->base);
        if (length > 0) {
            if (_write(stream->fd, stream->base, length) == length) {
                if (stream->flags & Update) stream->flags &= ~Write;
            } else {
                stream->flags |= Error;
                result = -1;
            }
        }
    }
    stream->cursor = stream->base;
    stream->left = 0;
    return result;
}


struct DescriptorInfo {
    long handle;
    unsigned char os_flags;
    unsigned char additional[31];
};
extern struct DescriptorInfo *__pioinfo[], __badioinfo;
extern struct Stream _iob[];
extern char *_stdbuf[];
extern int _cflush;
void *malloc(size_t);
void free(void *);
int _isatty(int);
enum { ExplicitBuffer = 0x400, TemporaryBuffer = 0x1000, ControlZ = 0x2000,
       TextMode = 0x80, DescriptorEof = 2 };
#define DESCRIPTOR(fd) ((fd) != -1 ? &__pioinfo[(fd) >> 5][(fd) & 31] : &__badioinfo)

int steam_filbuf(struct Stream *stream)
{
    if (!(stream->flags & (Read | Write | Update)) || (stream->flags & String))
        return -1;
    if (stream->flags & Write) {
        stream->flags |= Error;
        return -1;
    }
    stream->flags |= Read;
    if (!(stream->flags & AnyBuffer))
        _getbuf(stream);
    else
        stream->cursor = stream->base;
    stream->left = _read(stream->fd, stream->base, stream->buffer_size);
    if (stream->left == 0 || stream->left == -1) {
        stream->flags |= stream->left == 0 ? End : Error;
        stream->left = 0;
        return -1;
    }
    if (!(stream->flags & (Write | Update)) &&
        (DESCRIPTOR(stream->fd)->os_flags & (TextMode | DescriptorEof)) == (TextMode | DescriptorEof))
        stream->flags |= ControlZ;
    if (stream->buffer_size == 512 && (stream->flags & OwnedBuffer) &&
        !(stream->flags & ExplicitBuffer))
        stream->buffer_size = 4096;
    --stream->left;
    return *stream->cursor++ & 0xff;
}

void steam_getbuf(struct Stream *stream)
{
    ++_cflush;
    stream->base = malloc(4096);
    if (stream->base) {
        stream->flags |= OwnedBuffer;
        stream->buffer_size = 4096;
    } else {
        stream->flags |= Unbuffered;
        stream->base = (char *)&stream->small_buffer;
        stream->buffer_size = 2;
    }
    stream->cursor = stream->base;
    stream->left = 0;
}

void steam_freebuf(struct Stream *stream)
{
    if ((stream->flags & (Read | Write | Update)) && (stream->flags & OwnedBuffer)) {
        free(stream->base);
        stream->flags &= ~(OwnedBuffer | ExplicitBuffer);
        stream->base = stream->cursor = 0;
        stream->left = 0;
    }
}

void steam_ftbuf(int installed, struct Stream *stream)
{
    if (installed && (stream->flags & TemporaryBuffer)) {
        _flush(stream);
        stream->flags &= ~(TemporaryBuffer | UserBuffer);
        stream->buffer_size = 0;
        stream->base = stream->cursor = 0;
    }
}

int steam_stbuf(struct Stream *stream)
{
    int which;
    if (!_isatty(stream->fd)) return 0;
    if (stream == &_iob[1]) which = 0;
    else if (stream == &_iob[2]) which = 1;
    else return 0;
    ++_cflush;
    if (stream->flags & AnyBuffer) return 0;
    if (!_stdbuf[which] && !(_stdbuf[which] = malloc(4096))) {
        stream->cursor = stream->base = (char *)&stream->small_buffer;
        stream->left = stream->buffer_size = 2;
    } else {
        stream->cursor = stream->base = _stdbuf[which];
        stream->left = stream->buffer_size = 4096;
    }
    stream->flags |= Write | TemporaryBuffer | UserBuffer;
    return 1;
}
