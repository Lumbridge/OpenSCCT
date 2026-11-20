struct Face { int vertex[3], strip, trial_strip, trial; };
struct Edge { unsigned reserved; struct Face *left, *right; int a,b; struct Edge *next_a,*next_b; };
struct Edges { unsigned reserved; struct Edge **first; };
struct Start { struct Face *face; struct Edge *edge; unsigned char reverse; };
struct Strip { struct Start start; unsigned reserved[4]; int id, trial; };
struct FaceRef { struct Face *ptr; };
struct EdgeList { struct Edges *ptr; };


static __forceinline unsigned char belongs(const struct Strip *s, const struct Face *f)
{
    if (s->trial >= 0) return f->trial_strip == s->id;
    return f->strip == s->id;
}
extern unsigned char __fastcall shares_edge(const struct Strip *, struct FaceRef, struct EdgeList);

static __forceinline int marked(const struct Strip *s, const struct Face *f)
{
    return f->strip >= 0 || (s->trial >= 0 && f->trial == s->trial);
}
int __stdcall select_traversal(void *unused, struct Edges *edges, const struct Strip *s, struct Start *out)
{
    int vertex = s->start.reverse ? s->start.edge->b : s->start.edge->a;
    struct Face *face = 0;
    struct Edge *edge = edges->first[vertex];
    for (; edge; edge = edge->a == vertex ? edge->next_a : edge->next_b) {
        struct Face *left = edge->left, *right = edge->right;
        if (left && !belongs(s,left) && right && !marked(s,right)) {
            face = right;
            break;
        }
        if (right && !belongs(s,right) && left && !marked(s,left)) {
            face = left;
            break;
        }
    }
    out->face = face;
    out->edge = edge;
    if (edge) {
        struct FaceRef f = { face };
        struct EdgeList list = {edges};
        if (shares_edge(s, f, list)) out->reverse = edge->a == vertex;
        else out->reverse = edge->b == vertex;
    }
    return face != 0;
}
