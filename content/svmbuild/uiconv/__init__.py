"""P7 ui conversion: Magma pages (Menus/*.mgm), their texture atlases, the .mft bitmap fonts and the Magma strings.

- mgm.py      Magma package text (an XML dialect) to the svm.magma/1 JSON the client's page player reads
- mft.py      Magma Font glyph tables (.mft) to svm.font/1 JSON
- strings.py  the Magma localisation files (System/_PC_/Magma*.int) to JSON
- export.py   the whole step: every package, atlas, font page and string table under shared/ui/

The format of the output is described in docs/HUD.md.
"""
