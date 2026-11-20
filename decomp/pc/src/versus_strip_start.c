struct Face { int vertex[3], strip; };
struct Faces { unsigned reserved; struct Face **begin, **end; };
struct Edges;
struct FaceList { struct Faces *ptr; };
struct EdgeList { const struct Edges *ptr; };
struct Search { unsigned reserved[6]; float jump; unsigned char first; };
extern struct Face *other_face(const struct Edges *, int, int, const struct Face *);

int __fastcall first_face(struct Search *search, struct FaceList faces, struct EdgeList edges)
{
    unsigned i;
    int most = -1, best = -1;
    struct Face **begin = faces.ptr->begin;
    for (i = 0; i < (!begin ? 0 : (unsigned)(faces.ptr->end - begin)); ++i) {
        const struct Face *f = begin[i];
        int a = f->vertex[0], b = f->vertex[1], c;
        int missing = 0;
        if (!other_face(edges.ptr, a, b, f)) ++missing;
        c = f->vertex[2];
        if (!other_face(edges.ptr, b, c, f)) ++missing;
        if (!other_face(edges.ptr, c, a, f)) ++missing;
        if (missing > most) {
            most = missing;
            best = i;
        }
    }
    if (!most) return -1;
    return best;
}

extern const float one;
struct Face *__fastcall next_face(struct Search *search, struct FaceList faces, struct EdgeList edges)
{
    struct Face *result = 0;
    int start, i, n = !faces.ptr->begin ? 0 : faces.ptr->end - faces.ptr->begin;
    if (search->first) {
        start = first_face(search, faces, edges);
        search->first = 0;
    } else {
        start = (int)((n - one) * search->jump);
    }
    if (start == -1) start = (int)((n - one) * search->jump);
    i = start;
    do {
        if (faces.ptr->begin[i]->strip < 0) {
            result = faces.ptr->begin[i];
            break;
        }
        if (++i >= n) i = 0;
    } while (i != start);
    search->jump += 0.1f;
    if (search->jump > one) search->jump = 0.05f;
    return result;
}
