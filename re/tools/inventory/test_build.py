"""Tests for the coverage inventory on synthetic inputs. Run: python -I -B re/tools/inventory/test_build.py"""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build  # noqa: E402


def _func(name, flags, native=0, size=10, rep_offset=None):
    return {"name": name, "flags": flags, "native": native, "script_size": size, "file_bytes": size,
            "params": [], "locals": [], "return": None, "rep_offset": rep_offset, "sha1": "x" * 40,
            "line": 1, "latent_calls": []}


SYNTH_INDEX = {
    "totals": {"classes": 2},
    "classes": [
        {"package": "Engine", "class": "Actor", "super": "Core.Object", "flags": ["NativeReplication"],
         "native": True, "functions": [_func("Destroy", ["Native", "Final"], native=279, size=6)],
         "states": [], "replication": [], "vars": [], "defaults": {"defaults_values": 3},
         "sizes": {"export_bytes": 100, "script_bytes": 6}},
        {"package": "SBase", "class": "SPlayerController", "super": "Engine.Actor",
         "flags": ["NativeReplication"], "native": True,
         "functions": [
             _func("MyServerMove", ["Net", "Native", "Public"], native=1390, size=36, rep_offset=26),
             _func("BigServerMove", ["Net", "NetReliable", "Native", "Public"], native=1393),
             _func("MyLongClientAdjustPosition", ["Public"], size=400),
         ],
         "states": [{"name": "s_Dead", "flags": [], "labels": [["Begin", 0]], "script_size": 30,
                     "functions": [_func("BeginState", ["Defined"])], "latent_calls": [],
                     "ignores": [], "extends": None, "sha1": "y" * 40},
                    {"name": "s_Empty", "flags": [], "labels": [], "script_size": 1,
                     "functions": [], "latent_calls": [], "ignores": [], "extends": None,
                     "sha1": "z" * 40}],
         "replication": [
             {"reliable": False, "condition": "Role < ROLE_Authority",
              "members": ["MyServerMove"], "kinds": ["function"]},
             {"reliable": True, "condition": "Role == ROLE_Authority && bNetInitial",
              "members": ["SPawnType"], "kinds": ["variable"]},
         ],
         "vars": [], "defaults": {"defaults_values": 1}, "sizes": {"export_bytes": 1, "script_bytes": 1}},
    ],
}
SYNTH_NATIVES = {
    "SBase.SPlayerController.MyServerMove": {"index": 1390, "flags": 0x20440, "addr": 0x10AACEC0},
    "SBase.SPlayerController.BigServerMove": {"index": 1393, "flags": 0x204C0, "addr": 0x10AAE660},
    "Engine.Actor.Destroy": {"index": 279, "flags": 0x20401, "addr": 0x10A00000},
    "Engine.Actor.Spawn": {"index": 278, "flags": 0x20401, "addr": 0x10A00010},
}
SYNTH_REFLECT = {
    "modules": [{"name": "scct versus", "base": "0x10900000", "size": "0x00455000"},
                {"name": "Reloaded.Core.dll", "base": "0x64750000", "size": "0x0042e000"}],
    "functions": [
        {"path": "SBase.SPlayerController.MyServerMove", "func": "0x10aacec0"},
        {"path": "SBase.SPlayerController.BigServerMove", "func": "0x64773870"},
        {"path": "Engine.Actor.Destroy", "func": "0x10a00000"},
    ],
    "properties": [{"path": "SBase.SPlayerController.SPawnType", "kind": "ByteProperty",
                    "flagNames": ["Net"], "repOffset": 52, "repIndex": 70, "offset": 1920,
                    "arrayDim": 1, "elementSize": 1}],
    "classes": [],
}


class InventoryTests(unittest.TestCase):
    def setUp(self):
        self.inv = build.build(SYNTH_INDEX, SYNTH_NATIVES, SYNTH_REFLECT)
        self.by_id = {u["id"]: u for u in self.inv["units"]}

    def test_id_grammar(self):
        for uid in ("def:Engine.Actor", "def:SBase.SPlayerController",
                    "nat:SBase.SPlayerController.MyServerMove",
                    "uc:SBase.SPlayerController:MyLongClientAdjustPosition",
                    "uc:SBase.SPlayerController:s_Dead.BeginState",
                    "uc:SBase.SPlayerController:s_Dead@code",
                    "rep:SBase.SPlayerController.MyServerMove",
                    "rep:SBase.SPlayerController.SPawnType",
                    "nat:Engine.Actor.Spawn"):
            self.assertIn(uid, self.by_id)
        self.assertNotIn("uc:SBase.SPlayerController:s_Empty@code", self.by_id)

    def test_native_module_and_rva(self):
        u = self.by_id["nat:SBase.SPlayerController.MyServerMove"]
        self.assertEqual(u["native_module"], "scct versus")
        self.assertEqual(u["native_rva"], "0x001acec0")
        self.assertFalse(u["net_reliable"])
        h = self.by_id["nat:SBase.SPlayerController.BigServerMove"]
        self.assertEqual(h["native_module"], "Reloaded.Core.dll")
        self.assertIsNone(h["native_rva"])
        self.assertTrue(h["net_reliable"])
        self.assertTrue(self.by_id["nat:Engine.Actor.Spawn"]["orphan"])

    def test_rep_units(self):
        f = self.by_id["rep:SBase.SPlayerController.MyServerMove"]
        self.assertEqual(f["kind"], "rep-func")
        self.assertEqual(f["direction"], "client_to_server")
        self.assertFalse(f["net_reliable"])
        v = self.by_id["rep:SBase.SPlayerController.SPawnType"]
        self.assertEqual(v["kind"], "rep-var")
        self.assertEqual(v["direction"], "server_to_client")
        self.assertEqual(v["condition_flags"], ["bNetInitial"])
        self.assertEqual(v["rep_index"], 70)

    def test_slice_and_ancestors(self):
        self.assertEqual(self.by_id["def:SBase.SPlayerController"]["slice_reason"], "listed")
        self.assertEqual(self.by_id["def:Engine.Actor"]["slice_reason"], "ancestor")

    def test_subsystem(self):
        self.assertEqual(self.by_id["uc:SBase.SPlayerController:MyLongClientAdjustPosition"]["subsystem"],
                         "net.move")

    def test_output_is_stable(self):
        a = build.dump_inventory(self.inv)
        b = build.dump_inventory(build.build(SYNTH_INDEX, SYNTH_NATIVES, SYNTH_REFLECT))
        self.assertEqual(a, b)
        self.assertIn("## Units per package and kind", build.render_readme(self.inv))

    def test_native_table_parser(self):
        line = (" 1390 0x056e SBase.SPlayerController.MyServerMove                         "
                "flags 0x20440 func 0x10aacec0 2800ffff")
        m = build._NATIVE_LINE.match(line)
        self.assertIsNotNone(m)
        self.assertEqual(m.group(3), "SBase.SPlayerController.MyServerMove")


if __name__ == "__main__":
    unittest.main()
