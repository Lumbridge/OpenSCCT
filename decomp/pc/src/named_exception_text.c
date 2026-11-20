/* The named exception owns its message inline. Only its starting offset is known. */
struct named_exception {
    unsigned char prefix[12];
    char message[1];
};

const char *__fastcall named_exception_message(const struct named_exception *self)
{
    return self->message;
}
