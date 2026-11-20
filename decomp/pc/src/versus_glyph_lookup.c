struct Character { unsigned value; };
struct GlyphMap { unsigned short *pages[256]; };
struct Glyph { unsigned char prefix[6], width, remainder[17]; };
struct Font {
    unsigned char prefix[80];
    unsigned short maximum;
    struct Glyph *glyphs;
    struct GlyphMap map;
};
unsigned short __fastcall lookup_code(struct GlyphMap *map, unsigned unused, unsigned short code)
{
    unsigned short *page = map->pages[(unsigned char)(code >> 8)];
    if (!page) return 0;
    return page[code % 256];
}
extern unsigned short __fastcall lookup_glyph(struct GlyphMap *, struct Character);

unsigned __fastcall glyph_width(struct Font *font, struct Character code)
{
    unsigned short index = lookup_glyph(&font->map, code);
    if (index > font->maximum) return 0;
    return font->glyphs[index].width;
}
