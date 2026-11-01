"""Record authored death poses in an already-running SteeECombat sandbox.

The caller owns the sandbox/session lifetime. The fixture places and heals both
players; ordinary rifle fire causes the death. No health-zero/death-state write
selects the animation. Captures remain private game-derived data under local/.
"""
from __future__ import annotations

import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 're/tools/difftest'))
import combat
import combat_live
import combat_rig as cr
import livegame


def _pose(game, side, sampled_at):
    if not side or not side.get('pawn'):
        return None
    pawn = side['pawn']
    mi = pawn.get('MeshInstance')
    if not mi:
        return None
    header = game.p.read(mi + 0xb4, 12)
    if len(header) != 12:
        return {'error': 'SpaceBases header unreadable'}
    pointer, number, capacity = struct.unpack('<Iii', header)
    if not pointer or not 0 < number <= 256 or capacity < number:
        return {'error': 'SpaceBases unavailable', 'count': number, 'capacity': capacity}
    bones = game.p.read(pointer, number * 48)
    root = game.p.read(mi + livegame.MI_LOCK_MODE, livegame.MI_LOCK_SPAN)
    lock = livegame.decode_root_lock(root) if len(root) == livegame.MI_LOCK_SPAN else None
    if lock is not None:
        lock['lockKey'] = list(struct.unpack_from('<3f', root, livegame.MI_LOCK_KEY - livegame.MI_LOCK_MODE))
    return {'boneCount': number, 'arrays': {'180': bones.hex()},
            'rootLock': lock,
            'poseReadSameTick': game.time_seconds() == sampled_at}


def capture_death(session, tools, pid, out, *, victim='local', victim_team='spy', crouched=False,
                  post_death_frames=330, max_fire_frames=300):
    """Record a rifle death: local victim is shot by dummy merc, or local merc shoots target.

    Call inside box.play(victim_team if victim == 'local' else 'merc'). Both channel
    state and rendered SpaceBases/root locks are sampled for both players. A
    local victim ends the reusable play session; open a fresh one for another.
    """
    if victim not in ('local', 'target'):
        raise ValueError('victim must be local or target')
    if victim_team not in ('spy', 'merc'):
        raise ValueError('victim_team must be spy or merc')
    out = Path(out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    rig = cr.Rig(session)
    game = combat_live.CombatGame(session, rig)
    runner = combat.Runner(game, rig)
    local_team = victim_team if victim == 'local' else 'merc'
    scenario = {
        'local': {'team': local_team, 'floor': [-492., -2032., -1143.5], 'yaw': 16384,
                  'crouched': crouched if victim == 'local' else False},
        'target': {'team': 'merc' if victim == 'local' else victim_team,
                   'floor': [-492., -1732., -1143.5], 'yaw': 49152,
                   'crouched': crouched if victim == 'target' else False},
    }
    report = {'format': 'svm.original-death-pose/1', 'victim': victim, 'victimTeam': victim_team, 'crouched': crouched,
              'cause': 'ordinary rifle fire', 'setup': runner.setup(scenario), 'shots': [],
              'screenshots': [], 'outcome': 'incomplete'}
    runner.tick(90)
    reader = tools.headless.FrameReader(pid)
    died_at = None
    try:
        with (out / 'death-presentation.jsonl').open('w', encoding='utf-8') as stream:
            stream.write(json.dumps({'header': True, **report, 'offsets': game.offsets()}) + '\n')
            for frame in range(max_fire_frames + post_death_frames + 1):
                if died_at is None and 10 <= frame < max_fire_frames:
                    if victim == 'local' and (frame - 10) % 12 == 0:
                        runner.cmd(cr.CMD_DUMMY_FIRE, I0=cr.BONES['spine2'], I1=1,
                                   F0=0., F1=0., F2=0.)
                        report['shots'].append(frame)
                    elif victim == 'target' and frame == 10:
                        runner.cmd(cr.CMD_AIM, I0=cr.BONES['spine2'], I1=cr.BONES['pelvis'],
                                   F0=0., F1=0., F2=0.)
                        runner.cmd(cr.CMD_FIRE)
                        report['shots'].append(frame)
                sample = runner.tick()
                selected = sample[victim]
                hp = (selected or {}).get('pawn', {}).get('Health')
                if hp is not None and hp <= 0 and died_at is None:
                    died_at = frame
                    report['deathFrame'] = frame
                    runner.cmd(cr.CMD_FIRE_UP)
                record = {'frame': frame, 't': sample['t'], 'dt': sample['level'].get('m_dT'),
                          'stable': sample['stable'], 'torn': sample['torn'],
                          'local': runner.rec.side(sample['local']),
                          'target': runner.rec.side(sample['target']),
                          'localPose': _pose(game, sample['local'], sample['t']),
                          'targetPose': _pose(game, sample['target'], sample['t'])}
                stream.write(json.dumps(record, default=str) + '\n')
                since_death = frame - died_at if died_at is not None else None
                if frame == 0 or since_death in (0, 60, 180, 300):
                    width, height, pixels = reader.grab(timeout=10)
                    from PIL import Image
                    image = Image.frombytes('RGBA', (width, height), pixels, 'raw', 'BGRA').convert('RGB')
                    filename = f'frame-{frame:04d}.png'
                    image.save(out / filename)
                    report['screenshots'].append({'frame': frame, 'file': filename})
                if died_at is not None and since_death >= post_death_frames:
                    break
                if died_at is None and frame >= max_fire_frames:
                    raise RuntimeError('Ordinary rifle fire did not kill the selected victim within the bound')
            report['frames'] = frame + 1
            report['outcome'] = 'captured'
    except BaseException as error:
        report['error'] = str(error)
        raise
    finally:
        runner.release_inputs()
        reader.close()
        (out / 'summary.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    return report
