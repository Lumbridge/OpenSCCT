struct Cache { int *entries; int count; };
struct Face { int vertex[3]; };

static __forceinline int contains(const struct Cache *cache, int vertex)
{
    int i;
    for (i = 0; i < cache->count; ++i)
        if (cache->entries[i] == vertex)
            return 1;
    return 0;
}

static __forceinline void add_missing(struct Cache *cache, const int *vertex)
{
    int i, value;
    if (contains(cache, *vertex)) return;
    value = *vertex;
    for (i = cache->count - 2; i >= 0; --i)
        cache->entries[i + 1] = cache->entries[i];
    cache->entries[0] = value;
}

void __stdcall cache_face(struct Cache *cache, const struct Face *face)
{
    add_missing(cache, &face->vertex[0]);
    add_missing(cache, &face->vertex[1]);
    add_missing(cache, &face->vertex[2]);
}

int __stdcall count_cached(const struct Cache *cache, const struct Face *face)
{
    int hits = 0;
    if (contains(cache, face->vertex[0])) ++hits;
    if (contains(cache, face->vertex[1])) ++hits;
    if (contains(cache, face->vertex[2])) ++hits;
    return hits;
}

struct Edges;
extern struct Face *other_face(const struct Edges *, int, int, const struct Face *);
int __stdcall count_neighbors(const struct Face *face, const struct Edges *edges)
{
    int hits = 0;
    int a = face->vertex[0], b = face->vertex[1], c;
    if (other_face(edges, a, b, face)) ++hits;
    c = face->vertex[2];
    if (other_face(edges, b, c, face)) ++hits;
    if (other_face(edges, c, a, face)) ++hits;
    return hits;
}

struct Edge {
    unsigned reserved;
    struct Face *first_face, *second_face;
    int first, second;
    struct Edge *next_first, *next_second;
};
struct Edges { unsigned reserved; struct Edge **first; };
struct Edge *find_edge(const struct Edges *edges, int a, int b)
{
    struct Edge *edge = edges->first[a];
    while (edge) {
        if (edge->first == a) {
            if (edge->second == b) return edge;
            edge = edge->next_first;
        } else {
            if (edge->first == b) return edge;
            edge = edge->next_second;
        }
    }
    return 0;
}
struct Face *other_face(const struct Edges *edges, int a, int b, const struct Face *face)
{
    struct Edge *edge = find_edge(edges, a, b);
    if (!edge && a == b) return 0;
    if (edge->first_face == face) return edge->second_face;
    return edge->first_face;
}

struct Faces { unsigned reserved; struct Face **first, **end; };
unsigned char __stdcall face_present(const struct Face *face, const struct Faces *faces)
{
    unsigned i;
    for (i = 0; i < (faces->first ? (unsigned)(faces->end - faces->first) : 0); ++i) {
        const struct Face *other = faces->first[i];
        if (other->vertex[0] == face->vertex[0] &&
            other->vertex[1] == face->vertex[1] &&
            other->vertex[2] == face->vertex[2])
            return 1;
    }
    return 0;
}
