"""Prepare credited single reports and animation-timed mechanical reloads. Requires ffmpeg and numpy."""
from pathlib import Path
import subprocess
import wave
import json
import numpy as np

root = Path(__file__).resolve().parent
output = root / 'export/audio'
output.mkdir(exist_ok=True)
settings = json.loads((root / 'sources/audio/sources.json').read_text())
IDS = ('suppressed', 'automatic', 'magnum44', 'desert_eagle', 'unsuppressed')


def read(key):
    raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(root / 'sources/audio' / settings[key]['file']),
                                   '-f', 'f32le', '-ac', '1', '-ar', '44100', 'pipe:1'])
    return np.frombuffer(raw, dtype='<f4').copy()


def save(name, data):
    with wave.open(str(output / (name + '.wav')), 'wb') as recording:
        recording.setparams((1, 2, 44100, len(data), 'NONE', 'not compressed'))
        recording.writeframes((np.clip(data, -1, 1) * 32767).astype('<i2').tobytes())


for pistol in IDS:
    data = read(pistol)
    audible = np.flatnonzero(np.abs(data) > np.abs(data).max() * .025)
    data = data[max(0, audible[0] - 44):min(len(data), audible[-1] + 442)]
    data = data[:int(44100 * settings[pistol].get('max_seconds', 1.5))]
    data[-221:] *= np.linspace(1, 0, 221)
    data *= settings[pistol].get('peak', .92) / max(np.abs(data).max(), 1e-8)
    save(pistol + '_fire', data)

magazine = read('pistol_reload')
revolver = read('revolver_reload')
timing = {}
for pistol, seconds in zip(IDS, (2, 2.3, 2.8, 2.6, 2)):
    heavy = pistol == 'magnum44'
    fractions = [.12, .28, .58, .82] if heavy else [.14, .56, .66]
    sections = [(1.30, 1.56), (6.04, 6.32), (7.74, 7.99), (19.15, 19.43)] if heavy else [(0, .26), (.55, .87), (1.14, 1.48)]
    source = revolver if heavy else magazine
    result = np.zeros(round(seconds * 44100), dtype=float)
    for fraction, (start, end) in zip(fractions, sections):
        cue = source[round(start * 44100):round(end * 44100)].copy()
        cue -= cue.mean()
        audible = np.flatnonzero(np.abs(cue) > np.abs(cue).max() * .06)
        cue = cue[max(0, audible[0] - 22):]
        cue[:44] *= np.linspace(0, 1, 44)
        cue[-221:] *= np.linspace(1, 0, 221)
        cue *= .62 / max(np.abs(cue).max(), 1e-8)
        offset = round(fraction * seconds * 44100)
        count = min(len(cue), len(result) - offset)
        result[offset:offset + count] += cue[:count]
    result *= min(1, .82 / max(np.abs(result).max(), 1e-8))
    save(pistol + '_reload', result)
    timing[pistol] = {'seconds': seconds, 'events': [round(f * seconds, 6) for f in fractions]}
(output / 'timing.json').write_text(json.dumps(timing, indent=2) + chr(10))
