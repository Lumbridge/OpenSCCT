"""Binary fixtures for the original EAX3 listener ABI, independent of live audio hardware."""
from pathlib import Path
import importlib.util
import json
import struct

import pytest

SOURCE = Path(__file__).resolve().parents[2] / "tools/audio/reverb_reference.py"
SPEC = importlib.util.spec_from_file_location("reverb_reference", SOURCE)
reference = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(reference)


def test_native_listener_layout_preserves_signed_gain_vectors_and_mapped_flags():
    params = {"Room": -1000, "RoomHF": -2000, "RoomLF": -200, "ReflectionsPan": {"X": .5, "Y": -.25, "Z": .125},
              "ReverbPan": {"X": -.5, "Y": .25, "Z": -.125}, "RoomRolloffFactor": 0,
              "bEchoTimeScale": True, "bModulationTimeScale": True, "bDecayHFLimit": True}
    block = reference.listener_block(params)
    assert len(block) == 112
    assert struct.unpack_from("<iii", block, 12) == (-1000, -2000, -200)
    assert struct.unpack_from("<fff", block, 44) == (.5, -.25, .125)
    assert struct.unpack_from("<fff", block, 64) == (-.5, .25, -.125)
    assert struct.unpack_from("<f", block, 104)[0] == pytest.approx(.1)
    assert struct.unpack_from("<I", block, 108)[0] == 0xE0


def test_full_source_block_keeps_environment_zero_and_native_room_rolloff_minimum():
    values = reference.listener_values({"Environment": 21, "EnvironmentSize": 42.5, "RoomRolloffFactor": .7})
    assert values["Environment"] == 0
    assert values["EnvironmentSize"] == 42.5
    assert values["RoomRolloffFactor"] == .7
    assert reference.listener_values({"RoomRolloffFactor": -2})["RoomRolloffFactor"] == .1


def test_non_finite_native_values_are_rejected_before_emitting_a_driver_fixture():
    with pytest.raises(ValueError, match="DecayTime"):
        reference.listener_block({"DecayTime": float("nan")})


def test_inventory_keeps_distinct_native_blocks_and_exports_verified_null_branch(tmp_path):
    audio = tmp_path / "content/svmap/Example/audio"
    audio.mkdir(parents=True)
    (audio / "audio.json").write_text(json.dumps({"zones": [
        {"id": "NoEffect"}, {"id": "One", "effect": {"class": "SBase.EFFECT_Room", "params": {"DecayTime": .4}}},
        {"id": "Two", "effect": {"class": "SBase.EFFECT_Room", "params": {"DecayTime": .8}}}]}))
    output = tmp_path / "fixtures"
    result = reference.inventory(tmp_path / "content", output)
    assert result["zoneCount"] == 3 and result["effectZones"] == 2
    assert result["uniqueNativeBlocks"] == 2
    assert result["nullEffectZones"] == [{"map": "Example", "id": "NoEffect"}]
    assert len(list((output / "environments").glob("*.bin"))) == 2
    null = (output / "NullNative.eax3.bin").read_bytes()
    assert len(null) == 112
    assert struct.unpack_from("<f", null, 4)[0] == 42.5
    assert struct.unpack_from("<i", null, 12)[0] == -1000
    assert struct.unpack_from("<I", null, 108)[0] == 0x3F
