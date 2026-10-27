"""Conversion of one original map (.sdc) to an SVMap folder (spec/svmap/1.1).

source.py     opens the map: package, Level export, GE partition, every actor of Level.Actors with typed properties
gameplay.py   sim/gameplay.json (and the per-map class table sim/classes.json)
collision.py  sim/collision.bin
traversal.py  sim/traversal.json
render.py     render/ (world.glb from scctpkg's exporter, materials, lighting, fx, textures) and ui/mapinfo.json
build.py      the whole folder, with the manifest
"""
