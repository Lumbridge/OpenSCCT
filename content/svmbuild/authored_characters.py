"""Optional local character exports, installed before cosmetic textures are built.

No local manifest means the normal converted characters. Authored assets derived
from the user's game remain under ignored local/characters, outside release inputs.
"""
from pathlib import Path
import json
import shutil

from .writers.glb import read_glb

SOURCE = Path(__file__).resolve().parents[2] / 'local' / 'characters'
MODELS = {'spy': 'ATT_01', 'merc': 'DEF_01'}


def exports(root=SOURCE):
    root = Path(root)
    manifest = root / 'enabled.json'
    if not manifest.exists():
        return []
    names = json.loads(manifest.read_text(encoding='utf-8'))
    if not isinstance(names, list) or any(name not in MODELS for name in names):
        raise ValueError('enabled.json must list spy and/or merc')
    return [root / name / 'export' / (MODELS[name] + '.glb') for name in dict.fromkeys(names)]


def texture_files(glb, document):
    paths = {image['uri'] for image in document.get('images', []) if image.get('uri')}
    for material in document.get('materials', []):
        paths.update(value for value in material.get('extras', {}).values()
                     if isinstance(value, str) and value.startswith('textures/'))
    for uri in sorted(paths):
        path = (glb.parent / uri).resolve()
        if not path.is_relative_to(glb.parent.resolve()) or not path.is_file():
            raise ValueError(f'Missing or unsafe character texture: {uri}')
        yield path


def source_files(root=SOURCE):
    root = Path(root)
    result = [root / 'enabled.json'] if (root / 'enabled.json').exists() else []
    for glb in exports(root):
        document, _ = read_glb(glb.read_bytes())
        result.extend([glb, *texture_files(glb, document)])
    return sorted(set(result))


def bone_names(document):
    return {document['nodes'][index]['name'] for skin in document['skins'] for index in skin['joints']}


def clip_durations(document):
    accessors = document['accessors']
    return {animation['name']: max(accessors[s['input']].get('max', [0])[0]
                                  for s in animation['samplers'])
            for animation in document['animations']}


def install_models(chars_dir, root=SOURCE):
    prepared = []
    for glb in exports(root):
        target = Path(chars_dir) / 'SPerso' / glb.name
        document, _ = read_glb(glb.read_bytes())
        original, _ = read_glb(target.read_bytes())
        if bone_names(document) != bone_names(original):
            raise ValueError(f'{glb.name}: game skeleton changed')
        actual = clip_durations(document)
        for name, duration in clip_durations(original).items():
            if name not in actual or abs(actual[name] - duration) > .0001:
                raise ValueError(f'{glb.name}: missing or retimed gameplay clip {name}')
        if document.get('extras', {}).get('svmap') != original.get('extras', {}).get('svmap'):
            raise ValueError(f'{glb.name}: gameplay placement/animation metadata changed')
        prepared.append((glb, target, list(texture_files(glb, document))))
    # Validate the complete selection before touching the content output.
    for glb, target, textures in prepared:
        for texture in textures:
            dest = target.parent / texture.relative_to(glb.parent.resolve())
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(texture, dest)
        shutil.copy2(glb, target)
    return [target.name for _, target, _ in prepared]
