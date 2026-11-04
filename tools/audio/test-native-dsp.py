"""Exercise the actual native DSP and export source-bound impulse responses.

Requires NumPy/SciPy for offline analysis only. Run after Build-NativeAudio.ps1;
the game runtime does not depend on Python or these packages. The comparison
against Creative's independent renderer is performed by reverb_reference.py.
"""
from __future__ import annotations
import argparse
import ctypes as C
import json
import math
import os
from pathlib import Path
import sys

import numpy as np
from scipy.io import wavfile
from reverb_reference import listener_block


class Environment(C.Structure):
    _fields_ = [("values", C.c_float * 26), ("decay_hf_limit", C.c_int)]


def environment(p: dict) -> Environment:
    linear = lambda key: 10 ** (float(p.get(key, 0)) / 2000)
    pan = lambda key: [p.get(key, {}).get(axis, 0.0) for axis in ("X", "Y", "Z")]
    # The native all-parameter setter supplies already-scaled delays and levels;
    # EAX environment-size scaling flags apply only to individual size changes.
    values = [min(1, p.get("EnvironmentSize", 0) ** 3 / 16), p.get("EnvironmentDiffusion", 0),
              linear("Room"), linear("RoomHF"), linear("RoomLF"), p.get("DecayTime", 0),
              p.get("DecayHFRatio", 0), p.get("DecayLFRatio", 0), linear("Reflections"),
              p.get("ReflectionsDelay", 0), *pan("ReflectionsPan"), linear("Reverb"),
              p.get("ReverbDelay", 0), *pan("ReverbPan"), p.get("EchoTime", 0),
              p.get("EchoDepth", 0), p.get("ModulationTime", 0), p.get("ModulationDepth", 0),
              linear("AirAbsorptionHF"), p.get("HFReference", 0), p.get("LFReference", 0),
              max(0.1, p.get("RoomRolloffFactor", 0))]
    return Environment((C.c_float * 26)(*values), int(p.get("bDecayHFLimit", False)))


class Backend:
    def __init__(self, library: Path, rate=48000):
        self.rate = rate
        self.dll_directories = []
        if os.name == "nt":
            self.dll_directories.append(os.add_dll_directory(str(library.resolve().parent)))
            # Independent DSP-only builds link the same adjacent packaged dependency.
            packaged = Path("game/addons/scct_audio/bin/windows-x86_64").resolve()
            if packaged.is_dir(): self.dll_directories.append(os.add_dll_directory(str(packaged)))
        self.lib = C.CDLL(str(library.resolve()))
        signatures = {
            "scct_reverb_create": ([C.c_int], C.c_void_p),
            "scct_reverb_destroy": ([C.c_void_p], None),
            "scct_reverb_reset": ([C.c_void_p], None),
            "scct_reverb_latency_frames": ([C.c_void_p], C.c_int),
            "scct_reverb_last_error": ([C.c_void_p], C.c_char_p),
            "scct_reverb_set_environment": ([C.c_void_p, C.POINTER(Environment)], C.c_int),
            "scct_reverb_set_eax_environment": ([C.c_void_p, C.c_void_p], C.c_int),
            "scct_reverb_validate_eax_environment": ([C.c_void_p], C.c_int),
            "scct_reverb_backend_version": ([], C.c_char_p),
            "scct_eax3_source_gains": ([C.c_void_p, C.c_float, C.c_float, C.c_float, C.c_float, C.c_void_p], C.c_int),
            "scct_reverb_process": ([C.c_void_p, C.c_void_p, C.c_void_p, C.c_int], C.c_int),
            "scct_source_filter_create": ([C.c_int], C.c_void_p),
            "scct_source_filter_destroy": ([C.c_void_p], None),
            "scct_source_filter_configure": ([C.c_void_p, C.c_float, C.c_float, C.c_float], C.c_int),
            "scct_source_filter_process": ([C.c_void_p, C.c_void_p, C.c_void_p, C.c_int], C.c_int),
        }
        for name, (args, result) in signatures.items():
            fn = getattr(self.lib, name); fn.argtypes = args; fn.restype = result

    def handle(self, params):
        handle = self.lib.scct_reverb_create(self.rate)
        assert handle, self.lib.scct_reverb_last_error(None)
        try:
            assert self.lib.scct_reverb_latency_frames(handle) == 0
            self.configure(handle, params)
            return handle
        except BaseException:
            self.lib.scct_reverb_destroy(handle)
            raise

    def configure(self, handle, params):
        packet = C.create_string_buffer(listener_block(params))
        assert self.lib.scct_reverb_set_eax_environment(handle, packet), self.lib.scct_reverb_last_error(handle)

    def process(self, handle, data, blocks=(1024,), in_place=False):
        data = np.ascontiguousarray(data, dtype=np.float32)
        output = data.copy() if in_place else np.zeros_like(data)
        offset = block = 0
        while offset < len(data):
            count = min(blocks[block % len(blocks)], len(data) - offset)
            src = output if in_place else data
            assert self.lib.scct_reverb_process(handle, src[offset:].ctypes.data,
                output[offset:].ctypes.data, count), self.lib.scct_reverb_last_error(handle)
            offset += count; block += 1
        assert np.isfinite(output).all()
        return output

    def render(self, params, data, blocks=(1024,), in_place=False):
        handle = self.handle(params)
        try: return self.process(handle, data, blocks, in_place)
        finally: self.lib.scct_reverb_destroy(handle)


def checks(backend: Backend, fixture: dict) -> list[dict]:
    p = fixture["params"]
    data = np.zeros((backend.rate * 2, 2), np.float32)
    data[12000] = 3277 / 32768
    results = []
    def passed(name, **details): results.append({"check": name, "passed": True, **details})
    reference = backend.render(p, data)
    fragmented = backend.render(p, data, (1, 7, 64, 255, 513, 1024))
    difference = float(np.max(np.abs(reference - fragmented)))
    assert difference < 2e-7, difference
    passed("callback_partition_invariance", maximumError=difference, addedLatencyFrames=0)
    assert np.array_equal(reference, backend.render(p, data, in_place=True))
    passed("in_place_processing")
    silent = backend.render({**p, "Room": -10000}, data)
    assert np.max(np.abs(silent)) < np.max(np.abs(reference)) * 0.001
    passed("room_gain_attenuation")
    # Native late/early outputs are separately controlled, rather than collapsed to
    # the one wet knob used by the former Freeverb path.
    def isolated(early, late, reflections_delay=None, reverb_delay=None):
        config = {**p, "Reflections": early, "Reverb": late}
        if reflections_delay is not None: config["ReflectionsDelay"] = reflections_delay
        if reverb_delay is not None: config["ReverbDelay"] = reverb_delay
        return backend.render(config, data)
    early = isolated(p["Reflections"], -10000)
    late = isolated(-10000, p["Reverb"])
    both = isolated(p["Reflections"], p["Reverb"])
    sum_error = float(np.max(np.abs(early + late - both)))
    assert sum_error < 3e-7, sum_error
    assert np.sum(early * early) > 0 and np.sum(late * late) > 0
    passed("separate_early_and_late_paths", maximumError=sum_error)
    onset = lambda x: int(np.flatnonzero(np.max(np.abs(x), axis=1) > 1e-8)[0])
    delayed = isolated(p["Reflections"], -10000, reflections_delay=0.08)
    expected_shift = round((0.08 - p["ReflectionsDelay"]) * backend.rate)
    actual_shift = onset(delayed) - onset(early)
    assert abs(actual_shift - expected_shift) <= 2, (actual_shift, expected_shift)
    passed("early_delay_in_samples", expectedShift=expected_shift, actualShift=actual_shift)
    delayed_late = isolated(-10000, p["Reverb"], reverb_delay=0.08)
    # Creative compresses authored late delays >20ms by .85, floors at
    # 2.5ms, and rounds its feed taps up to 8 frames (original 0x1005fcc0).
    def feed_delay(delay):
        if delay > 0.02: delay = (delay - 0.02) * 0.85 + 0.02
        return (int(max(0.0025, delay) * backend.rate) + 7) & ~7
    expected_shift = feed_delay(0.08) - feed_delay(p["ReverbDelay"])
    actual_shift = onset(delayed_late) - onset(late)
    assert abs(actual_shift - expected_shift) <= 2, (actual_shift, expected_shift)
    passed("late_delay_in_samples", expectedShift=expected_shift, actualShift=actual_shift)
    handle = backend.handle(p)
    try:
        backend.process(handle, data[:13000])
        backend.lib.scct_reverb_reset(handle)
        after = backend.process(handle, np.zeros_like(data))
        assert np.max(np.abs(after)) < 1e-10
        invalid = environment(p); invalid.values[2] = math.nan
        assert not backend.lib.scct_reverb_set_environment(handle, C.byref(invalid))
    finally: backend.lib.scct_reverb_destroy(handle)
    passed("reset_clears_tails_and_invalid_parameter_rejected")
    handle = backend.handle(p)
    control = backend.handle(p)
    try:
        assert np.array_equal(backend.process(handle, data[:13000]), backend.process(control, data[:13000]))
        invalid = C.create_string_buffer(listener_block({**p, "EnvironmentDiffusion": 1.2}))
        assert not backend.lib.scct_reverb_set_eax_environment(handle, invalid)
        tail = np.zeros((backend.rate, 2), np.float32)
        assert np.array_equal(backend.process(handle, tail), backend.process(control, tail))
    finally:
        backend.lib.scct_reverb_destroy(handle)
        backend.lib.scct_reverb_destroy(control)
    passed("rejected_whole_packet_preserves_current_environment_and_tail")
    changed = {**p, "EnvironmentSize": 10, "RoomHF": -1200, "DecayTime": 1.2,
               "ReflectionsDelay": 0.025, "ReverbDelay": 0.08, "EchoDepth": 0.2,
               "ModulationDepth": 0.4}
    latest = {**p, "RoomHF": -700, "DecayTime": 0.8, "EnvironmentDiffusion": 0.6}
    rng = np.random.default_rng(2143)
    transition_input = rng.normal(0, 0.005, (32000, 2)).astype(np.float32)
    def scheduled(changes, blocks):
        handle = backend.handle(p)
        try:
            outputs = []; offset = 0
            for frame, parameters in changes:
                outputs.append(backend.process(handle, transition_input[offset:frame], blocks))
                backend.configure(handle, parameters)
                offset = frame
            outputs.append(backend.process(handle, transition_input[offset:], blocks))
            return np.concatenate(outputs)
        finally: backend.lib.scct_reverb_destroy(handle)
    changes = [(2000, changed), (2384, {**p, "Room": -3000}), (2800, latest)]
    continuous = scheduled(changes, (1024,))
    fragmented = scheduled(changes, (1, 7, 64, 255, 513, 1024))
    error = float(np.max(np.abs(continuous - fragmented)))
    assert error == 0, error
    passed("active_transition_callback_partition_invariance", maximumError=error)
    # A second target arriving while the old transition runs replaces the
    # pending packet; the discarded intermediate target must never sound.
    without_discarded = scheduled([(2000, changed), (2800, latest)], (1024,))
    assert np.array_equal(continuous, without_discarded)
    assert not np.array_equal(continuous, scheduled([(2000, changed)], (1024,)))
    passed("queued_zone_updates_keep_only_latest_target")
    handle = backend.handle(p); control = backend.handle(p)
    try:
        backend.process(handle, transition_input[:2000]); backend.process(control, transition_input[:2000])
        backend.configure(handle, changed); backend.configure(control, changed)
        backend.process(handle, transition_input[2000:2384]); backend.process(control, transition_input[2000:2384])
        invalid = C.create_string_buffer(listener_block({**p, "EnvironmentSize": 0.7}))
        assert not backend.lib.scct_reverb_set_eax_environment(handle, invalid)
        assert np.array_equal(backend.process(handle, transition_input[2384:]),
                              backend.process(control, transition_input[2384:]))
    finally:
        backend.lib.scct_reverb_destroy(handle); backend.lib.scct_reverb_destroy(control)
    passed("rejected_packet_preserves_active_transition_and_tail")
    # No configure call precedes this playback: Generic is the provider's own
    # initial environment. Its first later authored update must still ramp.
    generic = {"EnvironmentSize": 7.5, "EnvironmentDiffusion": 1, "Room": -10000,
               "RoomHF": -100, "DecayTime": 1.49, "DecayHFRatio": 0.83, "DecayLFRatio": 1,
               "Reflections": -2602, "ReflectionsDelay": 0.007, "Reverb": 200, "ReverbDelay": 0.011,
               "EchoTime": 0.25, "ModulationTime": 0.25, "AirAbsorptionHF": -5,
               "HFReference": 5000, "LFReference": 250,
               **{name: True for name in ("bDecayTimeScale", "bReflectionsScale", "bReflectionsDelayScale",
                                          "bReverbScale", "bReverbDelayScale", "bDecayHFLimit")}}
    handle = backend.lib.scct_reverb_create(backend.rate); control = backend.lib.scct_reverb_create(backend.rate)
    try:
        tone = np.sin(np.arange(16000) * 2 * np.pi * 530 / backend.rate).astype(np.float32) * 0.02
        tone = np.column_stack((tone, tone))
        backend.process(handle, tone); backend.process(control, tone)
        backend.configure(handle, generic)
        old = backend.process(control, tone[:1024]); updated = backend.process(handle, tone[:1024])
        assert np.array_equal(updated[0], old[0]), (updated[0], old[0])
        assert np.max(np.abs(updated - old)) > 1e-5
        assert np.sum(updated * updated) > 0
    finally:
        backend.lib.scct_reverb_destroy(handle); backend.lib.scct_reverb_destroy(control)
    passed("sounding_initial_generic_uses_live_first_authored_transition")
    # With the old input filter bypassed, Creative's zero-coefficient path
    # must equal the same retained network fed a 128-frame linear fade.
    # Sweeping the coefficient instead gives a measurably different response.
    bypass = {**p, "RoomHF": 0, "RoomLF": 0}
    endpoint_errors = {}
    fade_input = transition_input.copy()
    fade_input[2000:2128] *= (1 - np.arange(128, dtype=np.float32) / 128)[:, None]
    fade_input[2128:] = 0
    expected = backend.render(bypass, fade_input)
    for field in ("RoomHF", "RoomLF"):
        handle = backend.handle(bypass)
        try:
            before = backend.process(handle, transition_input[:2000])
            backend.configure(handle, {**bypass, field: -10000})
            after = backend.process(handle, transition_input[2000:], (1, 7, 64, 255, 513))
            actual = np.concatenate((before, after))
            error = float(np.max(np.abs(expected - actual)))
            assert error < 2e-7, (field, error)
            endpoint_errors[field] = error
        finally: backend.lib.scct_reverb_destroy(handle)
    # HP's opposite endpoint fades the new output IN; it does not sweep a
    # coefficient out of zero. Start silent so the expected input is analytic.
    handle = backend.handle({**bypass, "RoomLF": -10000})
    try:
        prefix = np.zeros((2000, 2), np.float32)
        backend.process(handle, prefix)
        backend.configure(handle, bypass)
        actual = backend.process(handle, transition_input, (1, 7, 64, 255, 513))
        fade_in = transition_input.copy()
        fade_in[:128] *= ((np.arange(128, dtype=np.float32) + 1) / 128)[:, None]
        reference_handle = backend.handle(bypass)
        try:
            backend.process(reference_handle, prefix)
            expected = backend.process(reference_handle, fade_in)
        finally: backend.lib.scct_reverb_destroy(reference_handle)
        error = float(np.max(np.abs(expected - actual)))
        assert error < 2e-7, error
        endpoint_errors["RoomLFRestore"] = error
    finally: backend.lib.scct_reverb_destroy(handle)
    passed("zero_filter_endpoints_use_original_output_fades", maximumErrors=endpoint_errors)
    filtered = backend.lib.scct_source_filter_create(backend.rate)
    try:
        rng = np.random.default_rng(913)
        noise = rng.normal(0, 0.01, (10000, 2)).astype(np.float32)
        output = np.empty_like(noise)
        assert backend.lib.scct_source_filter_configure(filtered, 1, 1, 5000)
        assert backend.lib.scct_source_filter_process(filtered, noise.ctypes.data, output.ctypes.data, len(noise))
        assert np.array_equal(noise, output)
        # Creative's one-pole coefficient targets POWER gain at HFReference,
        # quantized to Q15, rather than the former RBJ amplitude convention.
        for hz, expected, tolerance in [(50, 0.25, 0.002), (5000, 0.25 * math.sqrt(0.1), 0.0001)]:
            assert backend.lib.scct_source_filter_configure(filtered, 0.25, 0.1, 5000)
            tone = np.sin(np.arange(backend.rate) * 2 * np.pi * hz / backend.rate).astype(np.float32)
            tone = np.column_stack((tone, tone)); output = np.empty_like(tone)
            assert backend.lib.scct_source_filter_process(filtered, tone.ctypes.data, output.ctypes.data, len(tone))
            ratio = float(np.sqrt(np.mean(output[4000:] ** 2) / np.mean(tone[4000:] ** 2)))
            assert abs(ratio - expected) < tolerance, (hz, ratio, expected)
        assert not backend.lib.scct_source_filter_configure(filtered, math.nan, 1, 5000)
    finally: backend.lib.scct_source_filter_destroy(filtered)
    passed("source_filter_unity_and_analytic_reference_frequency_gains")
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--library", type=Path, required=True)
    parser.add_argument("--fixtures", type=Path, default=Path("local/reverb-parity/reference-parameters/parameters.json"))
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    backend = Backend(args.library)
    fixtures = json.loads(args.fixtures.read_text())
    room = next(f for f in fixtures["fixtures"] if f["name"] == "Room")
    report = {"library": str(args.library.resolve()), "backend": backend.lib.scct_reverb_backend_version().decode(),
              "sampleRate": backend.rate, "checks": checks(backend, room)}
    # Validate exact original 112-byte map packets, including original flags.
    environments = fixtures.get("environments", [])
    if isinstance(environments, dict): environments = list(environments.values())
    # The independent Creative property sweep rejects these whole EAX blocks;
    # their game behavior is to retain the preceding environment. Never sanitize
    # them into a new audible effect merely to make a renderer accept them.
    rejected = [case for case in environments if case["params"].get("EnvironmentSize", 0) < 1
                or not 0 <= case["params"].get("EnvironmentDiffusion", 0) <= 1
                or not 0.075 <= case["params"].get("EchoTime", 0) <= 0.25]
    rejected_ids = {case["id"] for case in rejected}
    for case in environments:
        packet = C.create_string_buffer(listener_block(case["params"]))
        accepted = backend.lib.scct_reverb_validate_eax_environment(packet)
        assert bool(accepted) == (case["id"] not in rejected_ids), case["id"]
        if not accepted: continue
        handle = backend.handle(case["params"]); backend.lib.scct_reverb_destroy(handle)
    report["validatedMapEnvironments"] = len(environments) - len(rejected)
    report["vendorRejectedWholeBlocks"] = sorted(rejected_ids)
    data = np.zeros((backend.rate * 8, 2), np.float32); data[12000] = 3277 / 32768
    for fixture in fixtures["fixtures"]:
        output = backend.render(fixture["params"], data)
        wavfile.write(args.out / (fixture["name"] + ".candidate.wav"), backend.rate, output)
    (args.out / "native-dsp-checks.json").write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))


if __name__ == "__main__": main()
