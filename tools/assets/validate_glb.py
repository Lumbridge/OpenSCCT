"""Dependency-free checks for staged external assets (not a full glTF validator)."""
import argparse
import json
import math
from pathlib import Path
import struct


def read_glb(path):
    raw = Path(path).read_bytes()
    if len(raw) < 20:
        raise ValueError('Truncated GLB header')
    magic, version, length = struct.unpack_from('<4sII', raw)
    if magic != b'glTF' or version != 2 or length != len(raw):
        raise ValueError('Invalid GLB header/version/length')
    chunks = []
    offset = 12
    while offset < length:
        if offset + 8 > length:
            raise ValueError('Truncated chunk header')
        size, kind = struct.unpack_from('<I4s', raw, offset)
        offset += 8
        if size % 4 or offset + size > length:
            raise ValueError('Invalid chunk length')
        chunks.append((kind, raw[offset:offset + size]))
        offset += size
    if not chunks or chunks[0][0] != b'JSON':
        raise ValueError('Missing first JSON chunk')
    return json.loads(chunks[0][1]), chunks[1:]


def write_glb(path, document, chunks):
    payload = json.dumps(document, separators=(',', ':'), allow_nan=False).encode()
    payload += b' ' * (-len(payload) % 4)
    body = b''.join(struct.pack('<I4s', len(data), kind) + data
                    for kind, data in [(b'JSON', payload), *chunks])
    Path(path).write_bytes(struct.pack('<4sII', b'glTF', 2, len(body) + 12) + body)


def validate(path, contract=None):
    contract = contract or {}
    document, chunks = read_glb(path)
    errors, warnings = [], []
    nodes = document.get('nodes', [])
    accessors = document.get('accessors', [])
    triangles = 0
    for mesh in document.get('meshes', []):
        for primitive in mesh.get('primitives', []):
            if primitive.get('mode', 4) != 4:
                errors.append('Only triangle primitives are supported by this pipeline')
                continue
            index = primitive.get('indices', primitive.get('attributes', {}).get('POSITION'))
            if index is None or not 0 <= index < len(accessors):
                errors.append('Missing or invalid geometry accessor')
                continue
            triangles += accessors[index]['count'] // 3
    if triangles == 0:
        errors.append('No triangle geometry')
    if triangles > contract.get('max_triangles', 100000):
        errors.append(f'Triangle budget exceeded: {triangles}')
    for item in [*document.get('images', []), *document.get('buffers', [])]:
        uri = item.get('uri')
        if uri and not uri.startswith('data:'):
            candidate = (Path(path).parent / uri).resolve()
            if not candidate.is_relative_to(Path(path).parent.resolve()) or not candidate.is_file():
                errors.append(f'Missing or out-of-package dependency: {uri}')
    joints = [nodes[i].get('name', '') for skin in document.get('skins', []) for i in skin['joints']]
    missing = set(contract.get('required_bones', [])) - set(joints)
    if missing:
        errors.append('Missing bones: ' + ', '.join(sorted(missing)))
    clips = {}
    for animation in document.get('animations', []):
        name = animation.get('name', '')
        if name in clips:
            errors.append(f'Duplicate clip: {name}')
        times = [accessors[s['input']] for s in animation.get('samplers', [])]
        duration = max((a.get('max', [0])[0] for a in times), default=0) - min(
            (a.get('min', [0])[0] for a in times), default=0)
        clips[name] = duration
        static_allowed = name in contract.get('allow_static_clips', []) and times and all(a.get('count', 0) > 0 for a in times)
        if not math.isfinite(duration) or duration < 0 or (duration == 0 and not static_allowed):
            errors.append(f'Empty or invalid clip: {name}')
        for channel in animation.get('channels', []):
            if not 0 <= channel.get('target', {}).get('node', -1) < len(nodes):
                errors.append(f'Invalid animation target in {name}')
    for name, duration in contract.get('required_clips', {}).items():
        if name not in clips:
            errors.append(f'Missing clip: {name}')
        elif duration is not None and abs(clips[name] - duration) > contract.get('duration_tolerance', .04):
            errors.append(f'Clip duration mismatch: {name}: {clips[name]:.4f}s, expected {duration}s')
    if joints:
        warnings.append('Skin/clip structure checked; contact points, root motion and gameplay timing require in-game review.')
    return {'file': str(Path(path).resolve()), 'triangles': triangles, 'bones': joints,
            'clips_seconds': clips, 'errors': errors, 'warnings': warnings, 'passed': not errors}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('glb', type=Path)
    parser.add_argument('--contract', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    report = validate(args.glb, json.loads(args.contract.read_text()) if args.contract else None)
    text = json.dumps(report, indent=2)
    print(text)
    if args.report:
        args.report.write_text(text + '\n', encoding='utf-8')
    raise SystemExit(0 if report['passed'] else 1)
