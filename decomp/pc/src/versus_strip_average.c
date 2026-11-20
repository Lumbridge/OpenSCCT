struct Face;
struct Strip {
    unsigned reserved[4];
    struct Face **first, **end;
    unsigned reserved_18[4];
    int excluded;
};
struct Strips { unsigned reserved; struct Strip **first, **end; };

float __stdcall average_faces(const struct Strips *strips)
{
    int n;
    int total = 0, i;
    float denominator;
    n = !strips->first ? 0 : strips->end - strips->first;
    for (i = 0; i < n; ++i) {
        struct Strip *strip = strips->first[i];
        int size = !strip->first ? 0 : strip->end - strip->first;
        total += size - strip->excluded;
    }
    denominator = n;
    return (float)total / denominator;
}
