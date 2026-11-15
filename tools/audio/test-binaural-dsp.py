"""Test actual Steam Audio DSP, callback partitioning, tails and 32-source CPU cost.

Run after Build-NativeAudio.ps1; NumPy is needed only by this offline test.
"""
from __future__ import annotations
import argparse
import ctypes as C
import json
import os
from pathlib import Path
import platform
import time
import numpy as np


class Backend:
    def __init__(self, path):
        self.directory = os.add_dll_directory(str(path.resolve().parent)) if os.name == "nt" else None
        self.lib = C.CDLL(str(path.resolve()))
        signatures = {
            "create": ([C.c_int], C.c_void_p), "destroy": ([C.c_void_p], None),
            "reset": ([C.c_void_p], None), "direction": ([C.c_void_p, C.c_float, C.c_float, C.c_float], C.c_int),
            "process": ([C.c_void_p, C.c_void_p, C.c_void_p, C.c_int], C.c_int),
            "pending": ([C.c_void_p], C.c_int), "latency_frames": ([], C.c_int), "version": ([], C.c_char_p),
        }
        for name, (args, result) in signatures.items():
            fn = getattr(self.lib, "scct_binaural_" + name)
            fn.argtypes = args; fn.restype = result
            setattr(self, name, fn)

    def render(self, data, direction=(0, 0, -1), partitions=(128,), inplace=False, rate=48000):
        handle = self.create(rate)
        assert handle, "HRTF/source initialization failed"
        try:
            assert self.direction(handle, *direction)
            output = data.copy() if inplace else np.zeros_like(data)
            offset = block = 0
            while offset < len(data):
                count = min(partitions[block % len(partitions)], len(data) - offset)
                source = output if inplace else data
                assert self.process(handle, source[offset:].ctypes.data, output[offset:].ctypes.data, count)
                offset += count; block += 1
            assert np.isfinite(output).all()
            assert not self.pending(handle), "Convolution tail/adapter never drained"
            return output
        finally:
            self.destroy(handle)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--library", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    backend = Backend(args.library)
    latency = backend.latency_frames()
    assert latency == 128
    data = np.zeros((8192, 2), np.float32)
    data[64] = 0.1 / np.sqrt(2)
    reference = backend.render(data)
    assert np.max(np.abs(reference)) > 0.001
    assert np.max(np.abs(reference[:64 + latency])) < 1e-7
    for partitions in [(1,), (17, 31, 127, 256, 3, 1024), (4096,)]:
        np.testing.assert_allclose(backend.render(data, partitions=partitions), reference, atol=1e-7, rtol=0)
    np.testing.assert_allclose(backend.render(data, inplace=True), reference, atol=1e-7, rtol=0)
    directions = {"front": (0, 0, -1), "back": (0, 0, 1), "above": (0, 1, 0),
                  "below": (0, -1, 0), "left": (-1, 0, 0), "right": (1, 0, 0), "coincident": (0, 0, 0)}
    rendered = {key: backend.render(data, value) for key, value in directions.items()}
    energies = {key: np.sum(value.astype(float) ** 2, axis=0).tolist() for key, value in rendered.items()}
    assert energies["left"][0] > energies["left"][1] * 1.5
    assert energies["right"][1] > energies["right"][0] * 1.5
    for other in ["back", "above", "below"]:
        assert not np.allclose(rendered[other], reference, atol=1e-5), other
    for rate in [24000, 44100]:
        assert np.max(np.abs(backend.render(data, rate=rate))) > 0.001
    handle = backend.create(48000)
    try:
        assert not backend.direction(handle, float("nan"), 0, -1)
        assert backend.direction(handle, 1e30, -1e30, 1e30)
        assert not backend.process(handle, None, None, 128)
        scratch = np.zeros_like(data)
        assert backend.process(handle, data.ctypes.data, scratch.ctypes.data, 128)
        backend.reset(handle)
        assert not backend.pending(handle)
        assert backend.process(handle, None, scratch.ctypes.data, len(scratch))
        assert np.max(np.abs(scratch)) == 0, "Reused voice leaked old convolution state"
    finally:
        backend.destroy(handle)
    # This is host evidence, not a claim about low-end devices. Measure each
    # 128-frame group of 32 sources, including Python/ctypes dispatch overhead.
    handles = [backend.create(48000) for _ in range(32)]
    assert all(handles)
    rng = np.random.default_rng(17)
    noise = (rng.normal(0, 0.01, (128, 2))).astype(np.float32)
    output = np.zeros_like(noise)
    durations = []
    try:
        for block in range(450):
            started = time.perf_counter_ns()
            for i, source in enumerate(handles):
                backend.direction(source, np.sin(i), 0.1, np.cos(i))
                assert backend.process(source, noise.ctypes.data, output.ctypes.data, 128)
            if block >= 75:
                durations.append((time.perf_counter_ns() - started) / 1e6)
    finally:
        for source in handles:
            backend.destroy(source)
    report = {"backend": backend.version().decode(), "host": platform.processor(), "sampleRate": 48000,
              "adapterLatencySamples": latency, "adapterLatencyMs": latency / 48,
              "checks": ["partition_invariance", "in_place", "left_right", "front_back_elevation", "tail_drain",
                         "reset_isolation", "coincident", "invalid_input", "24000_and_44100"],
              "directionEnergy": energies, "voices": 32, "callbackPeriodMs": 128 / 48,
              "groupDspMsMean": float(np.mean(durations)), "groupDspMsP99": float(np.percentile(durations, 99)),
              "groupDspMsMax": float(np.max(durations)),
              "groupDeadlineOverruns": sum(v > 128 / 48 for v in durations)}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
