"""Analyze original/candidate IR captures with an independent dry reference.

Requires NumPy and SciPy. Removes the median tail DC, aligns each capture on its
dry peak, subtracts the dry waveform, and reports normalized wet energy/onsets and
T20 extrapolations. These scalar comparisons do not assert waveform identity.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import struct
import numpy as np
from scipy.signal import butter, sosfilt


def read_wave(path: Path) -> tuple[int, np.ndarray]:
    data = path.read_bytes()
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError(f"{path}: not RIFF WAVE")
    offset = 12
    fmt = raw = None
    while offset + 8 <= len(data):
        tag, length = struct.unpack_from("<4sI", data, offset)
        body = data[offset + 8:offset + 8 + length]
        if tag == b"fmt ":
            fmt = body
        elif tag == b"data":
            raw = body
        offset += 8 + length + (length & 1)
    if fmt is None or raw is None:
        raise ValueError(f"{path}: missing fmt/data")
    encoding, channels, rate, _, _, bits = struct.unpack_from("<HHIIHH", fmt)
    if encoding == 3 and bits == 32:
        values = np.frombuffer(raw, "<f4").astype(np.float64)
    elif encoding == 1 and bits == 16:
        values = np.frombuffer(raw, "<i2").astype(np.float64) / 32768
    else:
        raise ValueError(f"{path}: unsupported encoding {encoding}/{bits}")
    return rate, values.reshape(-1, channels)


def prepare(path: Path):
    rate, pcm = read_wave(path)
    dc = np.median(pcm[-min(len(pcm), rate // 2):], axis=0)
    pcm -= dc
    peak = int(np.argmax(np.sum(pcm * pcm, axis=1)))
    return rate, pcm, dc, peak


def t20(pcm: np.ndarray, rate: int) -> float | None:
    power = np.mean(pcm * pcm, axis=1)
    energy = np.cumsum(power[::-1])[::-1]
    if not len(energy) or energy[0] <= 1e-20:
        return None
    db = 10 * np.log10(np.maximum(energy / energy[0], 1e-30))
    keep = (db <= -5) & (db >= -25)
    indices = np.flatnonzero(keep)
    if len(indices) < 32:
        return None
    slope, _ = np.polyfit(indices / rate, db[indices], 1)
    return float(-60 / slope) if slope < 0 else None


def compare_capture(path: Path, dry: Path) -> tuple[dict, np.ndarray]:
    rate, pcm, dc, peak = prepare(path)
    dry_rate, dry_pcm, dry_dc, dry_peak = prepare(dry)
    if rate != dry_rate or pcm.shape[1] != dry_pcm.shape[1]:
        raise ValueError("dry and wet rates/channel layouts differ")
    length = min(len(pcm) - peak, len(dry_pcm) - dry_peak)
    full = pcm[peak:peak + length]
    direct = dry_pcm[dry_peak:dry_peak + length]
    wet = full - direct
    # The dry path contains a small resampling kernel. All actual-map early delays
    # are >=2ms; ignore +/-1ms around the dry peak to avoid treating alignment error as a reflection.
    guard = rate // 1000
    wet[:guard] = 0
    dry_energy = float(np.sum(direct * direct))
    energy = float(np.sum(wet * wet))
    amplitude = np.max(np.abs(wet), axis=1)
    threshold = max(1.5 / 32768, float(np.max(np.abs(direct))) * .001)
    nonzero = np.flatnonzero(amplitude > threshold)
    bands = {}
    for name, low, high in (("125-250", 125, 250), ("500-1000", 500, 1000), ("2000-4000", 2000, 4000), ("4000-8000", 4000, 8000)):
        filtered = sosfilt(butter(4, (low, high), btype="bandpass", fs=rate, output="sos"), wet, axis=0)
        bands[name] = {"energyOverDry": float(np.sum(filtered * filtered)) / dry_energy,
                       "t20Seconds": t20(filtered, rate)}
    result = {"file": str(path), "sampleRate": rate, "frames": len(pcm), "dc": dc.tolist(),
              "dryReferenceDc": dry_dc.tolist(), "dryPeakFrame": peak, "dryPeakSeconds": peak / rate,
              "dryPeak": float(np.max(np.abs(direct))), "dryEnergy": dry_energy,
              "wetEnergyOverDry": energy / dry_energy,
              "wetOnsetSecondsAfterDry": int(nonzero[0]) / rate if len(nonzero) else None,
              "wetPeak": float(np.max(np.abs(wet))), "wetT20Seconds": t20(wet, rate), "bands": bands,
              "captureQuantization": "Vendor output observed at 16-bit steps despite float32 capture",
              "early80msEnergyOverDry": float(np.sum(wet[:int(rate * .08)] ** 2)) / dry_energy,
              "lateAfter80msEnergyOverDry": float(np.sum(wet[int(rate * .08):] ** 2)) / dry_energy}
    return result, wet


def analyze(directories: list[Path], dry: Path, output: Path):
    results = {}
    for directory in directories:
        for path in sorted(directory.glob("*.vendor.wav")):
            if path.name.startswith("Dry."):
                continue
            results[path.name.removesuffix(".vendor.wav")], _ = compare_capture(path, dry)
    if "Room" in results and "RoomRepeat" in results:
        one, two = results["Room"], results["RoomRepeat"]
        a = compare_capture(Path(one["file"]), dry)[1]
        b = compare_capture(Path(two["file"]), dry)[1]
        length = min(len(a), len(b))
        results["repeatComparison"] = {
            "normalizedWaveformErrorRms": float(np.sqrt(np.sum((a[:length] - b[:length]) ** 2) / np.sum(a[:length] ** 2))),
            "wetEnergyRelativeDifference": two["wetEnergyOverDry"] / one["wetEnergyOverDry"] - 1,
            "onsetDifferenceSeconds": two["wetOnsetSecondsAfterDry"] - one["wetOnsetSecondsAfterDry"]}
    output.write_text(json.dumps(results, indent=2), encoding="utf-8")
    return results


def compare_candidate(directories: list[Path], dry: Path, candidate_directory: Path, output: Path,
                      candidate_frame: int = 12004):
    """Compare wet-only candidate exports with independent vendor PCM, without a fitted gain."""
    dry_rate, direct, _, dry_peak = prepare(dry)
    rows = {}
    for directory in directories:
        for vendor in sorted(directory.glob("*.vendor.wav")):
            name = vendor.name.removesuffix(".vendor.wav")
            candidate = candidate_directory / (name + ".candidate.wav")
            if name == "Dry" or not candidate.exists():
                continue
            rate, captured, _, peak = prepare(vendor)
            candidate_rate, wet = read_wave(candidate)
            if rate != dry_rate or rate != candidate_rate or captured.shape[1] != wet.shape[1]:
                raise ValueError("Reference/candidate format mismatch")
            wet = wet[candidate_frame:]
            frames = min(len(captured) - peak, len(direct) - dry_peak, len(wet))
            original = captured[peak:peak + frames] - direct[dry_peak:dry_peak + frames]
            original[:rate // 1000] = 0
            quantized = np.rint(wet[:frames] * 32768) / 32768
            quantized[:rate // 1000] = 0
            delta = quantized - original
            active = (quantized != 0) | (original != 0)
            rows[name] = {"vendorFile": str(vendor), "candidateFile": str(candidate),
                "framesCompared": frames, "activeSampleCount": int(np.count_nonzero(active)),
                "differentSampleCount": int(np.count_nonzero(delta)),
                "maxErrorPcm16Lsb": float(np.max(np.abs(delta)) * 32768),
                "absoluteErrorRms": float(np.sqrt(np.mean(delta * delta))),
                "rawEnergyRatioCandidateOverOriginal": float(np.sum(wet[:frames] ** 2) / np.sum(original ** 2))}
    report = {"format": "openscct.independent-vendor-pcm-comparison/1", "candidateFirstComparedFrame": candidate_frame,
        "quantization": "nearest PCM16; measured vendor tail DC and independent dry waveform removed",
        "gainFitted": False, "cases": rows,
        "limits": ["Counts report actual differing samples; no long-silence parity percentage is inferred.",
            "The default four-frame offset after the input impulse is the independently observed dry HRTF delay.",
            "This comparison accepts wet-only candidate exports with exactly the same source input amplitude."]}
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, action="append", required=True)
    parser.add_argument("--dry", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--candidate-directory", type=Path)
    parser.add_argument("--candidate-frame", type=int, default=12004)
    args = parser.parse_args()
    if args.candidate_directory:
        report = compare_candidate(args.directory, args.dry, args.candidate_directory, args.out, args.candidate_frame)
        print(json.dumps({name: {key: value for key, value in row.items() if key in
            ("activeSampleCount", "differentSampleCount", "maxErrorPcm16Lsb", "absoluteErrorRms")}
            for name, row in report["cases"].items()}, indent=2))
        raise SystemExit(0)
    results = analyze(args.directory, args.dry, args.out)
    print(json.dumps({name: {key: value for key, value in result.items() if key in
        ("wetEnergyOverDry", "wetOnsetSecondsAfterDry", "wetT20Seconds", "wetEnergyRelativeDifference", "normalizedWaveformErrorRms")}
        for name, result in results.items()}, indent=2))
