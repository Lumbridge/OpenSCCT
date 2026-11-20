struct Stream { char *cursor; int left; char *base; int flags, fd, small_buffer, buffer_size; char *temporary_name; };
struct DescriptorInfo { long handle; unsigned char flags, rest[31]; };
extern struct DescriptorInfo *__pioinfo[];
#define OS_FLAGS(fd) (__pioinfo[(fd)>>5][(fd)&31].flags)
enum { Read=1, Write=2, Owned=8, Eof=16, Update=128, User=256, Configured=0x400, ControlZ=0x2000 };
int *_errno(void);
long _lseek(int, long, int);
long _ftell_lk(struct Stream *);
int _flush(struct Stream *);

long steam_ftell_lk(struct Stream *s)
{
    int fd = s->fd;
    long position, offset;
    if (s->left < 0) s->left = 0;
    position = _lseek(fd, 0, 1);
    if (position < 0) return -1;
    if (!(s->flags & (Owned | User))) return position - s->left;
    offset = (long)(s->cursor - s->base);
    if (s->flags & (Read|Write)) {
        if (OS_FLAGS(fd) & 0x80) {
            char *p;
            for (p = s->base; p < s->cursor; ++p)
                if (*p == '\n') ++offset;
        }
    } else if (!(s->flags & Update)) { *_errno() = 22; return -1; }
    if (!position) return offset;
    if (s->flags & Read) {
        if (!s->left) offset = 0;
        else {
            unsigned span = s->left + (unsigned)(s->cursor - s->base);
            if (OS_FLAGS(fd) & 0x80) {
                if (_lseek(fd,0,2) == position) {
                    char *p, *end = s->base + span;
                    for (p = s->base; p < end; ++p) if (*p == '\n') ++span;
                    if (s->flags & ControlZ) ++span;
                } else {
                    _lseek(fd,position,0);
                    if (span <= 512 && (s->flags & Owned) && !(s->flags & Configured)) span = 512;
                    else span = s->buffer_size;
                    if (OS_FLAGS(fd) & 4) ++span;
                }
            }
            position -= span;
        }
    }
    return position + offset;
}
int steam_fseek_lk(struct Stream *s, long offset, int origin)
{
    if (!(s->flags & (Read | Write | Update)) || (origin != 0 && origin != 1 && origin != 2)) {
        *_errno() = 22;
        return -1;
    }
    s->flags &= ~Eof;
    if (origin == 1) { offset += _ftell_lk(s); origin = 0; }
    _flush(s);
    if (s->flags & Update) s->flags &= ~(Read | Write);
    else if ((s->flags & Read) && (s->flags & Owned) && !(s->flags & Configured)) s->buffer_size = 512;
    return _lseek(s->fd, offset, origin) == -1 ? -1 : 0;
}
