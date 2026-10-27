"""Decoders for SCCT Versus package data that scctpkg does not cover yet.

Modules (each one has a command line, ``python -B -m scctformats.<name> --help``):

* ``ge``           traversal GEs (ledges, pipes, zip lines ...) stored in a map's Level export.
* ``smcollision``  the collision data that follows the render buffers in a UStaticMesh.
* ``classdefaults`` exact class default objects from the script packages (``System/_PC_/*.u``).

Outputs go under ``local/formats/`` (gitignored). Nothing here writes into the reference install.
"""

__all__ = ["ge", "smcollision", "classdefaults"]
