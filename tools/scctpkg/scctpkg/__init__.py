"""Reader for Splinter Cell: Chaos Theory Versus (UE2.5) package files.

Supports the game's obfuscated name/import/export tables and the zlib
container used by cooked maps (.sdc). See docs/ARCHITECTURE.md.
"""

from .package import Package, PackageError  # noqa: F401
