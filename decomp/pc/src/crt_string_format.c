typedef unsigned short WideChar;
typedef char *ArgumentList;
struct StringStream {
    char *cursor;
    int remaining;
    char *base;
    int flags;
    int file, char_buffer, buffer_size;
    char *temporary_name;
};
enum { FromString = 0x40, Read = 1, Write = 2, InternalBuffer = 8, MaximumCount = 0x7fffffff };
int _output(struct StringStream *, const char *, ArgumentList);
int _woutput(struct StringStream *, const WideChar *, ArgumentList);
int _input(struct StringStream *, const char *, ArgumentList);
int _winput(struct StringStream *, const WideChar *, ArgumentList);
int _flsbuf(int, struct StringStream *);
unsigned strlen(const char *);
unsigned wcslen(const WideChar *);

/* On 32-bit x86 the first variable argument follows the format pointer. */
#define AFTER_FORMAT ((ArgumentList)(&format + 1))
#define PUT_ZERO() \
    if (--stream.remaining >= 0) *stream.cursor++ = 0; \
    else _flsbuf(0, &stream)
#define FORMAT_NARROW(count, args) \
    struct StringStream stream; \
    int result; \
    stream.remaining = (int)(count); \
    stream.flags = FromString | Write; \
    stream.cursor = stream.base = dest; \
    result = _output(&stream, format, args); \
    if (dest) { PUT_ZERO(); } \
    return result

int steam_sprintf(char *dest, const char *format, ...) {
    FORMAT_NARROW(MaximumCount, AFTER_FORMAT);
}
int steam_vsprintf(char *dest, const char *format, ArgumentList args) {
    FORMAT_NARROW(MaximumCount, args);
}
int steam_snprintf(char *dest, unsigned count, const char *format, ...) {
    FORMAT_NARROW(count, AFTER_FORMAT);
}
int steam_vsnprintf(char *dest, unsigned count, const char *format, ArgumentList args) {
    FORMAT_NARROW(count, args);
}
int steam_vscprintf(const char *format, ArgumentList args) {
    struct StringStream stream;
    stream.cursor = stream.base = 0;
    stream.remaining = MaximumCount;
    stream.flags = FromString | Write;
    return _output(&stream, format, args);
}

#define FORMAT_WIDE(count, args) \
    struct StringStream stream; \
    int result; \
    stream.cursor = stream.base = (char *)dest; \
    stream.flags = FromString | Write; \
    stream.remaining = (int)(count); \
    result = _woutput(&stream, format, args); \
    PUT_ZERO(); \
    PUT_ZERO(); \
    return result

int steam_swprintf(WideChar *dest, const WideChar *format, ...) {
    FORMAT_WIDE(MaximumCount, AFTER_FORMAT);
}
int steam_snwprintf(WideChar *dest, unsigned count, const WideChar *format, ...) {
    FORMAT_WIDE(count * sizeof(WideChar), AFTER_FORMAT);
}
int steam_vsnwprintf(WideChar *dest, unsigned count, const WideChar *format, ArgumentList args) {
    FORMAT_WIDE(count * sizeof(WideChar), args);
}

#define SCAN_STRING(length, input) \
    struct StringStream stream; \
    stream.flags = FromString | Read | InternalBuffer; \
    stream.cursor = stream.base = (char *)text; \
    stream.remaining = (int)((length)(text) * sizeof(*text)); \
    return input(&stream, format, AFTER_FORMAT)

int steam_sscanf(const char *text, const char *format, ...) {
    SCAN_STRING(strlen, _input);
}
int steam_swscanf(const WideChar *text, const WideChar *format, ...) {
    SCAN_STRING(wcslen, _winput);
}
