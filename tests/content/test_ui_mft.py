"""svmbuild.uiconv.mft and strings on hand-built bytes (synthetic fonts and string tables)."""

from __future__ import annotations

import struct

import pytest
from svmbuild.uiconv import mft, strings


def _entry(ch, w, h, xoff, base, adv, page, u0, v0, u1, v1):
    return struct.pack("<HBBbbB4f", ch, w, h, xoff, base, adv, page + u0, page + v0, page + u1, page + v1)


def _font(name="Synth Sans 20", size=20, entries=(), tail=b""):
    head = b"Magma Font\x00" + struct.pack("<i", 0x82) + name.encode("latin-1") + b"\x00" + b"\xcd" * 7
    return head + struct.pack("<i", size) + b"\x00\xe0\x00" + b"".join(entries) + tail


def test_glyph_entries_and_pages():
    data = _font(entries=[
        _entry(32, 0, 0, 0, 0, 5, 0, 0.0, 0.0, 0.0, 0.0),
        _entry(65, 16, 15, -1, 15, 14, 0, 0.0, 0.84375, 0.0625, 0.9609375),
        _entry(66, 12, 15, 0, 15, 13, 1, 0.5, 0.25, 0.546875, 0.3671875),
        _entry(95, 10, 3, 0, -2, 10, 1, 0.0, 0.921875, 0.078125, 0.9453125),   # underscore: below the pen line
    ], tail=struct.pack("<H", 10) + bytes(21))   # out of order: the table ends there
    font = mft.parse_mft(data)
    assert font.name == "Synth Sans 20" and font.size == 20 and font.version == 0x82
    assert sorted(font.glyphs) == [32, 65, 66, 95]
    assert font.glyphs[95].base == -2
    a = font.glyphs[65]
    assert (a.width, a.height, a.xoff, a.base, a.advance, a.page) == (16, 15, -1, 15, 14, 0)
    assert font.glyphs[66].page == 1 and font.pages == 2
    table = mft.glyph_table(font, [(256, 128), (128, 64)])
    assert table[1] == {"c": 65, "x": 0, "y": 108, "w": 16, "h": 15, "page": 0, "xoff": -1, "base": 15, "adv": 14}
    assert table[2]["x"] == 64 and table[2]["y"] == 16 and table[2]["page"] == 1
    # A glyph on a page that was not found is left out.
    assert [g["c"] for g in mft.glyph_table(font, [(256, 128)])] == [32, 65]
    assert table[3]["base"] == -2


def test_names_of_pages_and_keys():
    assert mft.texture_prefix("Synth Sans 20") == "Synth_Sans_20_"
    assert mft.font_key("Synth Sans 20") == "Synth_Sans_20"


@pytest.mark.parametrize("data", [b"", b"Not a font", _font(entries=[]), b"Magma Font\x00\x82\x00"])
def test_bad_fonts(data):
    with pytest.raises(mft.MftError):
        mft.parse_mft(data)


def test_int_sections_keys_and_encodings():
    text = "; comment\r\n[A_Box]\r\nLabel=Hacking\r\nOther=a=b\r\n\r\n[HUD]\r\nTextChat1=Speak\r\n[A_Box]\r\nLabel=Again\r\n"
    table = strings.parse_int(text.encode("cp1252"))
    assert table == {"A_Box": {"Label": "Again", "Other": "a=b"}, "HUD": {"TextChat1": "Speak"}}
    e_acute = chr(0xE9)
    summer = f"{e_acute}t{e_acute}"
    assert strings.parse_int(f"[S]\nK={summer}".encode("utf-16")) == {"S": {"K": summer}}
    assert strings.parse_int(f"[S]\nK=caf{e_acute}".encode("cp1252")) == {"S": {"K": f"caf{e_acute}"}}
    assert strings.parse_int("K=no section\n") == {}


def _edge_to_edge_page():
    """Two 4x5 glyph cells side by side with no gap, each a solid colour, on a 16x8 page."""
    from PIL import Image

    page = Image.new("RGBA", (16, 8), (0, 0, 0, 0))
    page.paste((255, 0, 0, 255), (0, 0, 4, 5))
    page.paste((0, 255, 0, 255), (4, 0, 8, 5))
    glyphs = [
        {"c": 32, "x": 0, "y": 0, "w": 0, "h": 0, "page": 0, "xoff": 0, "base": 0, "adv": 4},
        {"c": 65, "x": 0, "y": 0, "w": 4, "h": 5, "page": 0, "xoff": 0, "base": 5, "adv": 5},
        {"c": 66, "x": 4, "y": 0, "w": 4, "h": 5, "page": 0, "xoff": 0, "base": 5, "adv": 5},
    ]
    return page, glyphs


def test_repack_keeps_every_pixel_and_pads_each_cell():
    page, glyphs = _edge_to_edge_page()
    pages, out = mft.repack_glyphs([page], glyphs, padding=2, width=32)
    assert len(pages) == 1
    new = pages[0]
    by_c = {g["c"]: g for g in out}
    for c, colour in ((65, (255, 0, 0, 255)), (66, (0, 255, 0, 255))):
        g = by_c[c]
        cell = new.crop((g["x"], g["y"], g["x"] + g["w"], g["y"] + g["h"]))
        assert set(cell.getdata()) == {colour}, "every glyph pixel is copied"
        # The ring round the cell is transparent, so a filtered draw of the exact cell cannot pick up a neighbour.
        ring = [new.getpixel((g["x"] - 1, y)) for y in range(g["y"] - 1, g["y"] + g["h"] + 1)]
        ring += [new.getpixel((g["x"] + g["w"], y)) for y in range(g["y"] - 1, g["y"] + g["h"] + 1)]
        ring += [new.getpixel((x, g["y"] - 1)) for x in range(g["x"], g["x"] + g["w"])]
        ring += [new.getpixel((x, g["y"] + g["h"])) for x in range(g["x"], g["x"] + g["w"])]
        assert all(p[3] == 0 for p in ring)
        assert (g["w"], g["h"], g["base"], g["adv"], g["page"]) == (4, 5, 5, 5, 0)
    assert by_c[32]["w"] == 0 and (by_c[32]["x"], by_c[32]["y"]) == (0, 0)


def test_repack_is_deterministic_and_wraps_rows():
    page, glyphs = _edge_to_edge_page()
    a_pages, a = mft.repack_glyphs([page], glyphs, padding=2, width=10)
    b_pages, b = mft.repack_glyphs([page], glyphs, padding=2, width=10)
    assert a == b and a_pages[0].tobytes() == b_pages[0].tobytes()
    by_c = {g["c"]: g for g in a}
    # Each padded cell is 8 wide, so a 10 wide page holds one per row.
    assert by_c[65]["y"] != by_c[66]["y"]
    assert a_pages[0].size[1] % 4 == 0


def test_repack_rejects_a_glyph_wider_than_the_page():
    page, glyphs = _edge_to_edge_page()
    with pytest.raises(ValueError):
        mft.repack_glyphs([page], glyphs, padding=2, width=6)
