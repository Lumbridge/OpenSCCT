struct Face { int vertex[3]; };

unsigned char repeated_face_index(const struct Face *face)
{
    if (face->vertex[0] == face->vertex[1]) return 1;
    if (face->vertex[0] == face->vertex[2]) return 1;
    if (face->vertex[1] == face->vertex[2]) return 1;
    return 0;
}

unsigned char __stdcall repeated_index(unsigned short a, unsigned short b, unsigned short c)
{
    if (a == b) return 1;
    if (a == c) return 1;
    if (b == c) return 1;
    return 0;
}

unsigned char __stdcall same_winding(const struct Face *face, int a, int b)
{
    if (face->vertex[0] == a) return face->vertex[1] == b;
    if (face->vertex[1] == a) return face->vertex[2] == b;
    return face->vertex[0] == b;
}
