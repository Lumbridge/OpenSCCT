"""svmbuild.uiconv.mgm on a hand-written Magma package (synthetic: the structure of the format, made-up content)."""

from __future__ import annotations

import pytest
from svmbuild.uiconv import mgm

PACKAGE = """<PACKAGE>
<NAME>Synth</NAME>
<VERSION>5.0</VERSION>
<PAGESIZE>640 480</PAGESIZE>
<MATERIALS>
<MATERIAL><ID>11</ID><NAME>white</NAME><STAGE><FILE>\\Textures\\Atlas.dds</FILE><REGION>0 0 32 32</REGION></STAGE></MATERIAL>
<MATERIAL><ID>12</ID><NAME>arrow</NAME><STAGE><FILE>\\Textures\\atlas.dds</FILE><REGION>32 0 48 16</REGION></STAGE></MATERIAL>
<MATERIAL><ID>13</ID><NAME>dot</NAME><STAGE><FILE>\\Textures\\Dot.dds</FILE><REGION>0 0 8 8</REGION></STAGE></MATERIAL>
</MATERIALS>
<FONTS><Pixmap><NAME>Test Font 20</NAME></Pixmap></FONTS>
<CHILDREN>
<Page>
<ID>100</ID><NAME>P_Test</NAME><FRAMERATE>10</FRAMERATE>
<CHILDREN>
<AREAINST>
<ID>101</ID><NAME>Box</NAME><HIDDEN>0</HIDDEN><MASKMODE>NOMASK</MASKMODE>
<KEYFRAMES><KEYFRAME><IDX>0</IDX><INTERPOLATION>None</INTERPOLATION><POSITION>55 213</POSITION><ROTATION>0</ROTATION>
<STATECOLOR>200 247 187 255</STATECOLOR><ACTIONS/></KEYFRAME></KEYFRAMES>
<LINK><PACKAGE>Synth</PACKAGE><AREA>200</AREA><TIMING>Independent time</TIMING></LINK>
</AREAINST>
<AREAINST>
<ID>102</ID><NAME>Synced</NAME><HIDDEN>1</HIDDEN><MASKMODE>NOMASK</MASKMODE><KEYFRAMES/>
<LINK><PACKAGE>Synth</PACKAGE><AREA>200</AREA><TIMING>Synchronized with page</TIMING></LINK>
</AREAINST>
<AREAINST>
<ID>103</ID><NAME>Elsewhere</NAME><MASKMODE>NOMASK</MASKMODE><KEYFRAMES/>
<LINK><PACKAGE>Other</PACKAGE><AREA>999</AREA><TIMING>None</TIMING></LINK>
</AREAINST>
<LIST>
<ID>104</ID><NAME>Rows</NAME><MASKMODE>NOMASK</MASKMODE><KEYFRAMES/>
<BUTTONCOUNT>4</BUTTONCOUNT><HEADERFOOTERPOS>Top and Bottom</HEADERFOOTERPOS>
<ITEMLINK><PACKAGE>Synth</PACKAGE><AREA>300</AREA></ITEMLINK><HEADERLINK/><FOOTERLINK/>
</LIST>
<UNKNOWNWIDGET><ID>105</ID></UNKNOWNWIDGET>
</CHILDREN>
</Page>
<Area>
<ID>200</ID><NAME>A_Box</NAME><FRAMERATE>20</FRAMERATE>
<CHILDREN>
<IMAGE>
<ID>201</ID><NAME>back</NAME><HIDDEN>0</HIDDEN><MASKMODE>USEMASK</MASKMODE>
<KEYFRAMES>
<KEYFRAME><IDX>6</IDX><INTERPOLATION>Circle</INTERPOLATION><ROTATIONCENTER>5 5</ROTATIONCENTER><TILING>-1.000 1.000</TILING>
<OFFSET>0.250 0.000</OFFSET><COLOR1>-2130706433</COLOR1><COLOR2>16777215</COLOR2><COLOR3>-1</COLOR3><COLOR4>255</COLOR4>
<LEFT>3</LEFT><RIGHT>84</RIGHT><TOP>3</TOP><BOTTOM>43</BOTTOM><ROTATION>90</ROTATION><STATECOLOR>255 255 255 255</STATECOLOR>
<ACTIONS><Stop><AREALINK><PACKAGE>Synth</PACKAGE><AREA>200</AREA></AREALINK></Stop></ACTIONS></KEYFRAME>
<KEYFRAME><IDX>0</IDX><INTERPOLATION>None</INTERPOLATION><COLOR1>-16777216</COLOR1>
<LEFT>3</LEFT><RIGHT>84</RIGHT><TOP>3</TOP><BOTTOM>43</BOTTOM><ROTATION>0</ROTATION><STATECOLOR>255 255 255 255</STATECOLOR>
<ACTIONS><GotoFrame><AREALINK><PACKAGE>Synth</PACKAGE><AREA>100</AREA></AREALINK><FRAMEIDX>3</FRAMEIDX></GotoFrame></ACTIONS></KEYFRAME>
</KEYFRAMES>
<MATERIALLINK><MATERIAL>12</MATERIAL><PACKAGE>Synth</PACKAGE></MATERIALLINK><BLENDINGMODE>Dodge 2X</BLENDINGMODE>
</IMAGE>
<IMAGE>
<ID>204</ID><NAME>shared</NAME><MASKMODE>NOMASK</MASKMODE><KEYFRAMES/>
<MATERIALLINK><MATERIAL>5</MATERIAL><PACKAGE>Other</PACKAGE></MATERIALLINK><BLENDINGMODE>Normal</BLENDINGMODE>
</IMAGE>
<IMAGE>
<ID>202</ID><NAME>broken</NAME><MASKMODE>SETMASK</MASKMODE><KEYFRAMES/>
<MATERIALLINK><MATERIAL>77</MATERIAL><PACKAGE>Synth</PACKAGE></MATERIALLINK><BLENDINGMODE>Normal</BLENDINGMODE>
</IMAGE>
<TEXT>
<ID>203</ID><NAME>Label</NAME><HIDDEN>0</HIDDEN><MASKMODE>NOMASK</MASKMODE>
<KEYFRAMES><KEYFRAME><IDX>0</IDX><INTERPOLATION>Linear</INTERPOLATION><TEXTCOLOR>255 255 255 128</TEXTCOLOR>
<SHADOWCOLOR>0 0 0 255</SHADOWCOLOR><HEIGHT>12.000000</HEIGHT><SHADOWOFFSETX>1</SHADOWOFFSETX><SHADOWOFFSETY>-1</SHADOWOFFSETY>
<OFFSETY>0.00</OFFSETY><LEADING>12</LEADING><TRACKING>45</TRACKING><LEFT>13</LEFT><RIGHT>72</RIGHT><TOP>25</TOP>
<BOTTOM>45</BOTTOM><ROTATION>0</ROTATION><STATECOLOR>255 255 255 255</STATECOLOR><ACTIONS/></KEYFRAME></KEYFRAMES>
<FONTLINK><NAME>Test Font 20</NAME></FONTLINK><ALIGNMENT>CENTER</ALIGNMENT><BOLD>0</BOLD><WRAPPING>1</WRAPPING>
<CLIPPING>1</CLIPPING><STRING>Hacking</STRING>
</TEXT>
</CHILDREN>
</Area>
<Button>
<ID>300</ID><NAME>B_Row</NAME><FRAMERATE>10</FRAMERATE>
<TIMINGS><ENABLED>0</ENABLED><PRESSED>10</PRESSED><DISABLED>20</DISABLED><SELECTED>30</SELECTED><OVER>40</OVER></TIMINGS>
<CHILDREN/>
</Button>
</CHILDREN>
</PACKAGE>
"""


@pytest.fixture(scope="module")
def doc():
    return mgm.parse_mgm(PACKAGE.encode("latin-1"))


def test_package_header_materials_and_fonts(doc):
    assert doc["format"] == "svm.magma/1"
    assert doc["name"] == "Synth" and doc["version"] == "5.0"
    assert doc["pageSize"] == [640, 480]
    assert [m["name"] for m in doc["materials"]] == ["white", "arrow", "dot"]
    assert doc["materials"][1] == {"id": 12, "name": "arrow", "file": "\\Textures\\atlas.dds", "texture": "atlas",
                                   "region": [32, 0, 48, 16]}
    assert doc["fonts"] == ["Test Font 20"]
    # Two spellings of one atlas are one texture.
    assert mgm.textures_used(doc) == ["Atlas", "Dot"]


def test_areas_pages_and_buttons(doc):
    assert [(a["name"], a["kind"]) for a in doc["areas"]] == [("P_Test", "page"), ("A_Box", "area"),
                                                             ("B_Row", "button")]
    assert doc["pages"] == {"P_Test": 0}
    assert doc["areas"][1]["frameRate"] == 20
    assert doc["areas"][2]["timings"] == {"enabled": 0, "pressed": 10, "disabled": 20, "selected": 30, "over": 40}
    assert doc["skippedWidgets"] == {"UNKNOWNWIDGET": 1}


def test_instances_link_areas_by_index(doc):
    box, synced, elsewhere, rows = doc["areas"][0]["widgets"]
    assert box["kind"] == "inst" and box["area"] == 1 and box["sync"] is False
    assert box["keys"] == [{"f": 0, "interp": "none", "pos": [55, 213], "rot": 0, "state": [200, 247, 187, 255]}]
    assert synced["hidden"] is True and synced["sync"] is True and synced["timing"] == "Synchronized with page"
    assert elsewhere["area"] == {"package": "Other", "id": 999}
    assert rows["kind"] == "list" and rows["rows"] == 4 and rows["item"] == 2
    assert rows["header"] is None and rows["headerFooter"] == "Top and Bottom"


def test_image_keys_are_sorted_with_raw_abgr_colours_and_actions(doc):
    back = doc["areas"][1]["widgets"][0]
    assert back["mask"] == "use" and back["blend"] == "Dodge 2X" and back["material"] == 1
    first, second = back["keys"]
    assert first["f"] == 0 and second["f"] == 6
    # COLORn is a signed 0xAABBGGRR int: stored unsigned; missing corners repeat COLOR1.
    assert first["colors"] == [0xFF000000] * 4
    assert second["colors"] == [0x80FFFFFF, 0x00FFFFFF, 0xFFFFFFFF, 0x000000FF]
    assert second["interp"] == "circle" and second["rect"] == [3, 3, 84, 43] and second["rot"] == 90
    assert second["tiling"] == [-1.0, 1.0] and second["offset"] == [0.25, 0.0] and second["rotCenter"] == [5, 5]
    assert first["actions"] == [{"op": "goto", "area": 0, "frame": 3}]
    assert second["actions"] == [{"op": "stop", "area": 1}]


def test_text_widget(doc):
    label = doc["areas"][1]["widgets"][3]
    assert label["kind"] == "text" and label["font"] == "Test Font 20" and label["align"] == "center"
    assert label["text"] == "Hacking" and label["flags"] == ["wrapping", "clipping"]
    (key,) = label["keys"]
    assert key["color"] == [255, 255, 255, 128] and key["shadow"] == [0, 0, 0, 255]
    assert key["shadowOffset"] == [1, -1] and key["height"] == 12.0 and key["tracking"] == 45
    assert key["leading"] == 12 and key["offsetY"] == 0.0 and key["interp"] == "linear"


def test_unresolved_references_are_reported(doc):
    assert doc["areas"][1]["widgets"][1]["material"] == {"package": "Other", "id": 5}
    assert doc["areas"][1]["widgets"][2]["material"] is None
    assert doc["unresolved"] == ["material 77"]


def test_not_a_package():
    with pytest.raises(mgm.MagmaError):
        mgm.parse_mgm(b"<HTML></HTML>")
    with pytest.raises(mgm.MagmaError):
        mgm.parse_mgm(b"not xml at all <")


def test_duplicate_area_ids_are_rejected():
    twice = "<PACKAGE><NAME>X</NAME><CHILDREN><Area><ID>1</ID><NAME>A</NAME></Area>" \
            "<Area><ID>1</ID><NAME>B</NAME></Area></CHILDREN></PACKAGE>"
    with pytest.raises(mgm.MagmaError):
        mgm.parse_mgm(twice)
