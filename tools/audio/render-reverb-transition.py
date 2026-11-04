"""Replay independent vendor transition inputs through the native HRvb C ABI.

Inputs and raw EAX packets come from Probe-OriginalEax captures, which remain
local. A write cursor identifies the first input frame the vendor can change;
the play cursor refers to audio already mixed and must not schedule the update.
"""
import argparse
import ctypes as C
import hashlib
import json
from pathlib import Path, PureWindowsPath
import runpy

import numpy as np
from scipy.io import wavfile

Backend = runpy.run_path(str(Path(__file__).with_name("test-native-dsp.py")))["Backend"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--library", type=Path, required=True)
    parser.add_argument("--reference-root", type=Path, default=Path("local/reverb-parity"))
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    capture_root = args.reference_root / "transition-reference"
    captures = json.loads((capture_root / "manifest.json").read_text())
    by_name = {case["case"]: case for case in captures}
    if len(by_name) != len(captures): raise ValueError("Duplicate reference capture case")
    cases = [
        ("ToneRoom", "ToneInput.wav", "Room", None),
        ("ToneGainStep", "ToneInput.wav", "Room", "transition-reference/Mute.eax3.bin"),
        ("HangarToRoom", "DoubleInput.wav", "Hangar", "reference-parameters/Room.eax3.bin"),
    ]
    manifest = []
    for name, input_name, initial_name, target_name in cases:
        capture = by_name[name]
        metadata_file = capture_root / (name + ".vendor.json")
        metadata = json.loads(metadata_file.read_text())
        if PureWindowsPath(capture["initial"]).name != initial_name + ".eax3.bin":
            raise ValueError(f"{name}: unexpected initial packet")
        if bool(capture["target"]) != bool(target_name): raise ValueError(f"{name}: unexpected transition")
        if target_name and PureWindowsPath(capture["target"]).name != Path(target_name).name:
            raise ValueError(f"{name}: unexpected target packet")
        if metadata.get("EAX3SetAll") != "0x00000000" or metadata.get("inputBytes") != 112:
            raise ValueError(f"{name}: unsuccessful original 112-byte parameter setup")
        frame = metadata.get("transitionWriteCursorFrames", 0)
        if target_name and (metadata.get("EAX3TransitionSet") != "0x00000000" or type(frame) is not int):
            raise ValueError(f"{name}: missing successful transition and frame cursor")
        # The harness divides the DS mono PCM16 cursor by nBlockAlign=2
        # before recording *Frames. Do not divide it by the float WAV stride.
        if target_name and "transitionWriteCursorFrames" not in metadata:
            raise ValueError(f"{name}: byte cursors are not frame cursors")
        input_file = capture_root / input_name
        rate, data = wavfile.read(input_file)
        vendor_rate, vendor_data = wavfile.read(capture_root / (name + ".vendor.wav"))
        if rate != 48000 or vendor_rate != rate or data.ndim != 2 or data.shape[1] != 2:
            raise ValueError(f"{name}: expected the reference harness's 48 kHz stereo WAVs")
        if target_name and not 0 <= frame < len(data): raise ValueError(f"{name}: cursor outside source input")
        if data.dtype == np.int16: data = data.astype(np.float32) / 32768
        else: data = data.astype(np.float32)
        if data.ndim == 1: data = np.column_stack((data, data))
        backend = Backend(args.library, rate)
        initial = (args.reference_root / "reference-parameters" / (initial_name + ".eax3.bin")).read_bytes()
        handle = backend.lib.scct_reverb_create(rate)
        assert handle
        try:
            assert backend.lib.scct_reverb_set_eax_environment(handle, C.create_string_buffer(initial))
            if target_name:
                prefix = backend.process(handle, data[:frame])
                target = (args.reference_root / target_name).read_bytes()
                assert backend.lib.scct_reverb_set_eax_environment(handle, C.create_string_buffer(target))
                audio = np.concatenate((prefix, backend.process(handle, data[frame:])))
            else:
                target = b""
                audio = backend.process(handle, data)
            wavfile.write(args.out / (name + ".candidate.wav"), rate, audio)
            manifest.append({"case": name, "backend": backend.lib.scct_reverb_backend_version().decode(),
                "inputSha256": hashlib.sha256(input_file.read_bytes()).hexdigest(),
                "initialPacketSha256": hashlib.sha256(initial).hexdigest(),
                "targetPacketSha256": hashlib.sha256(target).hexdigest() if target else None,
                "setAtSourceFrame": frame if target else None, "sampleRate": rate,
                "cursorProvenance": {"file": str(metadata_file), "field": "transitionWriteCursorFrames",
                    "unit": "frames", "secondaryPcmBlockAlignBytes": 2} if target else None})
        finally:
            backend.lib.scct_reverb_destroy(handle)
    (args.out / "render-manifest.json").write_text(json.dumps(manifest, indent=2))
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__": main()
