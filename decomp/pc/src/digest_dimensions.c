/* Width queries on the SHA-1 and MD5 implementations. The adjacent slots
   return the raw digest length and twice that length respectively. */
struct digest_module;

enum { sha1_bytes = 20, md5_bytes = 16 };

unsigned int __fastcall sha1_digest_width(const struct digest_module *self)
{
    return sha1_bytes;
}

unsigned int __fastcall sha1_double_digest_width(const struct digest_module *self)
{
    return 2 * sha1_bytes;
}

unsigned int __fastcall md5_digest_width(const struct digest_module *self)
{
    return md5_bytes;
}

unsigned int __fastcall md5_double_digest_width(const struct digest_module *self)
{
    return 2 * md5_bytes;
}
