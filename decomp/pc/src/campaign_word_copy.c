struct Word { unsigned value; };
struct Source { const struct Word *word; };

struct Word *__fastcall copy_76f7c0(struct Word *out, struct Source from)
{
    *out = *from.word;
    return out;
}
