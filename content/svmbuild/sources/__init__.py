"""Readers of the original install used by the pipeline steps.

These modules sit on top of the vendored scctpkg (tools/scctpkg) and the decoders in re/formats/scctformats. They
import those packages and add only what the steps need on top: typed property values, the fields of a UModel that
scctpkg skips, animation notifies and root tracks, and per-process caches. Nothing here writes into the install.
"""
