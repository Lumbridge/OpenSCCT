"""Capture an original ledge kill using placement and normal Jump/Use inputs.

Requires an existing SteeECombat map and the private RE+ sandbox tools. The
original stays hidden and muted; only this run's processes are cleaned up.
Game-derived poses and images remain under the selected repository's local/.
"""
import argparse
from pathlib import Path
import json
import sys


def capture(session, tools, pid, out):
    import livegame
    import combat
    import combat_live
    import combat_rig as cr
    from capture_original_death import _pose

    rig = cr.Rig(session)
    game = combat_live.CombatGame(session, rig)
    runner = combat.Runner(game, rig)
    inputs = livegame.LiveGame(session)
    inputs.enable_traversal(True)
    report = {'format': 'svm.original-ledge-pose/1', 'outcome': 'incomplete',
              'input': 'normal Jump then Use', 'setupOnly': 'placement, initial spawn, standing, speed',
              'limitation': 'Merc is the original combat rig dummy without a viewport/PlayerTick.',
              'transitions': [], 'screenshots': []}
    reader = None
    try:
        runner.release_inputs()
        runner.cmd(cr.CMD_REMOVE)
        runner.cmd(cr.CMD_TELEPORT, F0=-2450., F1=1305., F2=-1127.5, I1=16384)
        runner.tick(45)
        runner.local_stance(False)
        inputs.set_fast_speed(True)
        runner.cmd(cr.CMD_TELEPORT, F0=-2450., F1=1305., F2=-1127.5, I1=16384)
        target = {'team': 'merc', 'floor': [-2450., 1410., -807.5], 'yaw': 49152}
        report['targetSpawn'] = runner.spawn_target(target)
        report['targetPlace'] = runner.place_target(target)
        report['targetWalking'] = runner.target_walking()
        runner.tick(120)
        first = runner.tick()
        report['initial'] = {side: runner.rec.side(first[side]) for side in ('local', 'target')}
        if not (report['initial']['local']['cs'] or '').startswith('s_PlayerWalkingSt') or report['initial']['local']['crouch']:
            raise RuntimeError('Local spy did not reach standing walking state before normal jump')
        reader = tools.headless.FrameReader(pid)
        hung_at = use_at = died_at = None
        last = None
        with (out / 'ledge-presentation.jsonl').open('w', encoding='utf-8') as stream:
            stream.write(json.dumps({'header': True, **report, 'offsets': game.offsets()}) + '\n')
            inputs.press_jump()
            for frame in range(720):
                sample = runner.tick()
                local = runner.rec.side(sample['local'])
                target = runner.rec.side(sample['target'])
                key = (local['cs'], target['cs'], local['anim'][0][1] if local['anim'] else None,
                       target['anim'][0][1] if target['anim'] else None, target['hp'])
                record = {'frame': frame, 't': sample['t'], 'dt': sample['level'].get('m_dT'),
                          'stable': sample['stable'], 'torn': sample['torn'], 'local': local, 'target': target,
                          'localPose': _pose(game, sample['local'], sample['t']),
                          'targetPose': _pose(game, sample['target'], sample['t'])}
                stream.write(json.dumps(record, default=str) + '\n')
                if key != last:
                    report['transitions'].append({'frame': frame, 't': sample['t'], 'key': key,
                                                  'localLoc': local['loc'], 'targetLoc': target['loc']})
                    print(json.dumps(report['transitions'][-1]), flush=True)
                    last = key
                if local['cs'] == 'OnLedge' and hung_at is None:
                    hung_at = frame
                    report['hungFrame'] = frame
                if hung_at is not None and use_at is None and frame >= hung_at + 60:
                    game.write_input('bUsing', 1)
                    use_at = frame
                    report['useFrame'] = frame
                if use_at is not None and frame == use_at + 3:
                    game.write_input('bUsing', 0)
                if target['hp'] is not None and target['hp'] <= 0 and died_at is None:
                    died_at = frame
                    report['deathFrame'] = frame
                if frame in (0, 90, 180, 270, 360, 480, 600) or frame == died_at:
                    from PIL import Image
                    w, h, pixels = reader.grab(timeout=10)
                    name = f'frame-{frame:04d}.png'
                    Image.frombytes('RGBA', (w, h), pixels, 'raw', 'BGRA').convert('RGB').save(out / name)
                    report['screenshots'].append({'frame': frame, 'file': name})
                if hung_at is None and frame >= 180:
                    raise RuntimeError('Normal Jump did not reach OnLedge within 180 sampled frames')
                if died_at is not None and frame >= died_at + 180:
                    break
        report['frames'] = frame + 1
        if died_at is None:
            raise RuntimeError('Natural Jump/Use did not complete a lethal ledge pull within 720 sampled frames')
        report['outcome'] = 'captured'
    except BaseException as error:
        report['error'] = str(error)
        raise
    finally:
        try:
            runner.release_inputs()
        finally:
            if reader:
                reader.close()
            (out / 'ledge-summary.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[2],
                        help='checkout containing the original capture helpers (defaults to this checkout)')
    parser.add_argument('--out', type=Path, required=True,
                        help='new capture directory under repo/local; relative paths are relative to --repo')
    parser.add_argument('--sandbox', type=Path, help='existing original sandbox; defaults to sandbox.py setting')
    parser.add_argument('--re-tools', type=Path, help='private RE+ automation tools; defaults to sandbox.py setting')
    args = parser.parse_args()
    repo = args.repo.resolve()
    if not (repo / 're/tools/difftest/sandbox.py').is_file():
        parser.error('--repo must contain re/tools/difftest/sandbox.py')
    out = (repo / args.out).resolve()
    if not out.is_relative_to(repo / 'local') or out == repo / 'local':
        parser.error('--out must be a new directory under --repo/local; captures contain original game data')
    if out.exists():
        parser.error('--out already exists; choose a new capture directory')
    sys.path[:0] = [str(repo / 're/tools/difftest'), str(repo / 'tools/benchmark')]
    import sandbox as sb
    import livegame

    sandbox = str(args.sandbox.resolve()) if args.sandbox else sb.DEFAULT_SANDBOX
    re_tools = str(args.re_tools.resolve()) if args.re_tools else sb.DEFAULT_RE_TOOLS
    out.mkdir(parents=True, exist_ok=False)
    tools = sb.load_tools(re_tools)
    box = sb.Sandbox(tools, sandbox, fps=60, wait_idle=30)
    report = {'map': 'SteeECombat', 'muted': True, 'outcome': 'incomplete',
              'binaries': sb.binaries(sandbox)}
    paths = [Path(sandbox) / 'System' / n for n in ('Reloaded_PlayLevel.ini', 'Reloaded_Editor.ini')]
    saved = {p: p.read_bytes() for p in paths if p.is_file()}
    livegame.fine_timer(True)
    try:
        with box:
            report['mapSource'] = box.load_map('SteeECombat')
            with box.play('spy', lock_timeout=30) as (session, info):
                report['capture'] = capture(session, tools, info['gamePid'], out)
        report['outcome'] = 'captured'
    except BaseException as error:
        report['error'] = str(error)
        raise
    finally:
        livegame.fine_timer(False)
        for path, original in saved.items():
            path.write_bytes(original)
        report['session'] = box.report
        (out / 'summary.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(json.dumps({'outcome': report['outcome'], 'error': report.get('error')}), flush=True)


if __name__ == '__main__':
    main()
