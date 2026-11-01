"""Install OpenSCCT's authored sidearms and deterministically build their finishes and audio."""
from pathlib import Path
import json
import shutil
import wave

SOURCE = Path(__file__).resolve().parents[1] / 'assets' / 'sidearms' / 'export'
IDS = ('suppressed', 'automatic', 'magnum44', 'desert_eagle', 'unsuppressed')
FINISHES = ('night_ops', 'cryo', 'woodland', 'desert', 'digital', 'red_tiger', 'blue_tiger', 'golden')


def source_files():
    return sorted(p for p in SOURCE.rglob('*') if p.is_file())


def install_models(chars_dir: Path):
    from .cosmetics import _paint_job
    from .fitted import SurfaceMesh, ARMS
    target = chars_dir / 'OpenScctSidearms'
    shutil.copytree(SOURCE, target, dirs_exist_ok=True)
    from .sidearm_models import assemble
    retail=chars_dir/"SWeaponFirst"
    for image in (retail/"textures").glob("*.png"):
        if image.name.startswith("cosmetic_") and "1DEF_01" not in image.name:continue
        shutil.copy2(image,target/"textures"/image.name)
    finish_count = 0
    for pistol in IDS:
        assemble(chars_dir,pistol)
        # The world weapon is the fitting reference. First-person assemblies also
        # contain hands: their independently selected suit finish must never enter
        # the weapon loop. Only bake textures actually used by weapon primitives.
        mesh_path = target / (pistol + '.glb')
        mesh = SurfaceMesh(mesh_path)
        if ARMS in mesh.tris:
            raise ValueError(f'{pistol}: weapon fitting mesh contains the arm atlas')
        for stem in sorted(mesh.tris):
            image = target / 'textures' / (stem + '.png')
            for finish in FINISHES:
                _paint_job((str(image), str(image.with_name(f'cosmetic_{finish}_{image.name}')),
                            finish, image.stem, (1024, 1024), str(mesh_path)))
                finish_count += 1
    arm_finishes = install_arm_finishes(chars_dir)
    return {'models': 2 * len(IDS), 'finishes': finish_count, 'icons': len(IDS), 'arm_finishes': arm_finishes}


def install_arm_finishes(chars_dir: Path):
    """The new 2K sleeves retain the merc's existing independently selected body finishes."""
    from concurrent.futures import ThreadPoolExecutor
    from .cosmetics import _MERC_SUITS, _paint_job
    source = chars_dir / 'OpenScctSidearms/textures/OpenScctSidearms_arms_arms_baseColor.png'
    jobs = [(str(source), str(source.with_name(f'cosmetic_{pattern}_{source.name}')), pattern,
             source.stem, (2048, 2048), str(chars_dir / 'OpenScctSidearms/arms_base.glb'))
            for pattern in sorted(set(_MERC_SUITS))]
    with ThreadPoolExecutor(max_workers=2) as pool:
        return len(list(pool.map(_paint_job, jobs)))


def install_audio(audio_dir: Path):
    """Install credited single-shot reports and reload recordings aligned to the authored mechanisms."""
    folder = audio_dir / 'wav' / 'OpenScctSidearms'
    folder.mkdir(parents=True, exist_ok=True)
    shutil.copy2(SOURCE / 'CREDITS.txt', folder / 'CREDITS.txt')
    sounds = {}
    for pistol in IDS:
        for action in ('fire', 'reload'):
            name = pistol + '_' + action
            target = folder / (name + '.wav')
            shutil.copy2(SOURCE / 'audio' / (name + '.wav'), target)
            with wave.open(str(target), 'rb') as recording:
                count = recording.getnframes()
                rate = recording.getframerate()
                assert rate == 44100 and recording.getnchannels() == 1 and recording.getsampwidth() == 2
            sounds['OpenScctSidearms.'+name] = {
                'kind':'wave','flags':0,'file':'wav/OpenScctSidearms/'+name+'.wav',
                # Match Gun_Def's asset gain; reload handling stays below the reports.
                'loop':False,'positional':True,'radius':0,'volume':2.5118864 if action=='fire' else 1,
                'duration':count/rate,'frames':count,'rate':rate,'bits':16,'channels':1,
            }
    (audio_dir/'sidearms.json').write_text(json.dumps({'format':'svm.audio/1','sounds':sounds},sort_keys=True),encoding='utf-8')
    return {'sounds':len(sounds)}
