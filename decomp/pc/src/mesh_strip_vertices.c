struct Face { int vertex[3], strip, experiment_strip, experiment; };
struct Strip { unsigned reserved[7]; int id, experiment; };
struct FaceRef { struct Face *ptr; };

static __forceinline int has_vertex(const struct Face *face, int v)
{
    return v == face->vertex[0] || v == face->vertex[1] || v == face->vertex[2];
}

int distinct_vertex(const struct Face *a, const struct Face *b)
{
    if (!has_vertex(a, b->vertex[0])) return b->vertex[0];
    if (!has_vertex(a, b->vertex[1])) return b->vertex[1];
    if (!has_vertex(a, b->vertex[2])) return b->vertex[2];
    return -1;
}

static __forceinline int collect_vertex(const struct Face *a, const int *vertex, int *first, int *second)
{
    if (has_vertex(a, *vertex)) {
        if (*first == -1) {
            *first = *vertex;
        } else {
            *second = *vertex;
            return 1;
        }
    }
    return 0;
}

void shared_vertices(const struct Face *a, const struct Face *b, int *first, int *second)
{
    *first = -1;
    *second = -1;
    if (collect_vertex(a, &b->vertex[0], first, second)) return;
    if (collect_vertex(a, &b->vertex[1], first, second)) return;
    collect_vertex(a, &b->vertex[2], first, second);
}

void __fastcall mark_face(const struct Strip *strip, struct FaceRef face)
{
    if (strip->experiment >= 0) {
        face.ptr->experiment = strip->experiment;
        face.ptr->experiment_strip = strip->id;
    } else {
        face.ptr->experiment = -1;
        face.ptr->strip = strip->id;
    }
}

struct WordVector { unsigned char allocator; unsigned short *begin, *end, *limit; };
struct WordPosition { unsigned short *ptr; };

int __fastcall word_count(const struct WordVector *v)
{
    if (!v->begin) return 0;
    return v->end - v->begin;
}

unsigned short *__fastcall erase_words(struct WordVector *v, struct WordPosition first, struct WordPosition last)
{
    unsigned short *out = first.ptr;
    unsigned short *in = last.ptr;
    unsigned short *end = v->end;
    while (in != end) *out++ = *in++;
    v->end = out;
    return first.ptr;
}
