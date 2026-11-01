"""Capture original spy stance bones and local sound routing on the hidden sandbox.

Normal input drives the animation; memory reads observe the result. The original
stays muted. Outputs are private, game-derived evidence under local/.
"""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import struct
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT / 're/tools/difftest'), str(ROOT / 'tools/benchmark')]
import livegame
import record
import sandbox as sb
from capture_original_audio import capture as capture_audio


def presented_pose(game, reader, previous_present, previous_time, timeout=2.0):
    """Read channels and SpaceBases after a new Present, rejecting a tick/present that changes during the read.

    TimeSeconds advances before pose construction. A stable simulation sample alone can therefore contain the
    new channels with the previous frame's bones. Waiting on Present observes completion of that render without
    pausing or modifying the original. Each attempt is bounded, and discarded reads remain counted in evidence.
    """
    deadline = time.perf_counter() + timeout
    rejected = 0
    while time.perf_counter() < deadline:
        present = reader.presents()
        if present == previous_present:
            time.sleep(0.0002)
            continue
        frame_time = game.time_seconds()
        if frame_time == previous_time:
            previous_present = present
            continue
        # Present has already completed the frame; do not add the simulation reader's pre-render delay.
        sample = game.sample(frame_time, time.perf_counter() - game.delay)
        if sample is not None and sample['stable']:
            mi = sample['pawn']['MeshInstance']
            pointer, number, capacity = struct.unpack('<Iii', game.p.read(mi + 0xb4, 12))
            if number != 43 or not pointer or capacity < number:
                raise RuntimeError(f'Unexpected spy SpaceBases array: {pointer}, {number}, {capacity}')
            bones = game.p.read(pointer, number * 48)
            same_bones = game.p.read(pointer, number * 48) == bones
            after_time = game.time_seconds()
            after_present = reader.presents()
            if same_bones and after_time == frame_time and after_present == present:
                sample.update(t=frame_time, torn=rejected, arrays={'180': bones.hex()},
                              poseReadSameTick=True, poseReadSamePresent=True,
                              present=present, presentAfterRead=after_present,
                              levelTimeAfterRead=after_time, rejectedPoseReads=rejected)
                return sample, present
        rejected += 1
        previous_present = present
    raise TimeoutError('No stable channels/SpaceBases sample after Present within %.1fs' % timeout)


def capture_pose(game, tools, pid, out):
    runner = record.Runner(game)
    game.enable_traversal(True)
    directions = ('BT_Up', 'BT_Down', 'BT_Left', 'BT_Right')
    buttons = (*directions, 'bCrouch', 'bRoll')
    runner.release_all(buttons)
    # SteeE's spy spawn is a low passage: it legitimately refuses standing.
    # The established combat lane has a clear floor and enough headroom.
    start = runner.tick()
    game.teleport([-492., -2032., -1143.5 + start['pawn']['CollisionHeight'] + 80.], 16384)
    game.set_view(0, 16384)
    runner.tick(180)
    runner.stance(True)
    runner.wait_still(180)
    plan = [('crouched-idle', 60, ()), ('crouched-forward', 90, ('BT_Up',)),
            ('crouched-stop', 30, ()), ('crouched-left', 30, ('BT_Left',)),
            ('crouched-right', 30, ('BT_Right',)),
            ('stand-press', 3, ('bCrouch',)), ('stand-settle', 60, ()),
            ('standing-run', 90, ('BT_Up',)), ('roll-press', 3, ('BT_Up', 'bRoll')),
            ('roll-finish', 90, ('BT_Up',)), ('stop', 45, ())]
    screenshots = []
    reader = tools.headless.FrameReader(pid)
    last_present = reader.presents()
    try:
        with (out / 'spy-presentation.jsonl').open('w', encoding='utf-8') as stream:
            stream.write(json.dumps(dict(header=True, format='svm.original-pose/1',
                                        synchronization='present-counter+stable-level-clock+stable-bones',
                                        offsets=game.offsets(), traversal=game.traversal_offsets())) + '\n')
            frame = 0
            for phase, count, held in plan:
                if phase == 'stand-settle':
                    runner.stance(False)
                    game.set_fast_speed(True)
                for _ in range(count):
                    for button in buttons:
                        game.write_byte(button, int(button in held))
                    sample, last_present = presented_pose(game, reader, last_present, runner.prev_t)
                    runner.prev_t = sample['t']
                    sample.update(phase=phase, frame=frame,
                                  observedStance='crouched' if sample['pawn']['bIsCrouched'] else 'standing')
                    stream.write(json.dumps(sample, default=str) + '\n')
                    frame += 1
                width, height, pixels = reader.grab(timeout=10)
                from PIL import Image
                image = Image.frombytes('RGBA', (width, height), pixels, 'raw', 'BGRA').convert('RGB')
                filename = phase + '.png'
                image.save(out / filename)
                screenshots.append(dict(phase=phase, file=filename, frame=frame - 1,
                                        stance=sample['observedStance']))
    finally:
        runner.release_all(buttons)
        reader.close()
    return dict(frames=frame, screenshots=screenshots,
                synchronization='present-counter+stable-level-clock+stable-bones')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', default='local/original-presentation-20261101')
    parser.add_argument('--map', choices=('SteeE', 'SteeECombat'))
    parser.add_argument('--sandbox', default=sb.DEFAULT_SANDBOX)
    parser.add_argument('--mode', choices=('poses', 'death'), default='poses')
    parser.add_argument('--victim', choices=('local', 'target'), default='local')
    parser.add_argument('--victim-team', choices=('spy', 'merc'), default='spy')
    parser.add_argument('--crouched', action='store_true')
    parser.add_argument('--audio', action='store_true')
    args = parser.parse_args()
    args.map = args.map or ('SteeECombat' if args.mode == 'death' else 'SteeE')
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    tools = sb.load_tools(sb.DEFAULT_RE_TOOLS)
    report = dict(format='svm.original-presentation/1', binaries=sb.binaries(args.sandbox),
                  map=args.map, muted=True, inputs='normal controller buttons', outcome='incomplete')
    box = sb.Sandbox(tools, args.sandbox, fps=60, wait_idle=30)
    ini_paths = [Path(args.sandbox) / 'System' / name
                 for name in ('Reloaded_PlayLevel.ini', 'Reloaded_Editor.ini')]
    saved_ini = {path: path.read_bytes() for path in ini_paths if path.is_file()}
    livegame.fine_timer(True)
    try:
        with box:
            report['mapSource'] = box.load_map(args.map)
            side = ('merc' if args.victim == 'target' else args.victim_team) if args.mode == 'death' else 'spy'
            with box.play(side, lock_timeout=30) as (session, info):
                if args.mode == 'death':
                    from capture_original_death import capture_death
                    report['death'] = capture_death(session, tools, info['gamePid'], out / 'death',
                                                    victim=args.victim, victim_team=args.victim_team, crouched=args.crouched)
                else:
                    game = livegame.LiveGame(session)
                    with ThreadPoolExecutor(max_workers=1) as pool:
                        audio = pool.submit(capture_audio, session.attach(), out / 'audio.json', 30) if args.audio else None
                        report['pose'] = capture_pose(game, tools, info['gamePid'], out)
                        if audio is not None:
                            result = audio.result()
                            report['audio'] = dict(observations=len(result['observations']), seconds=result['seconds'])
        report['outcome'] = 'captured'
    except BaseException as error:
        report['error'] = str(error)
        raise
    finally:
        livegame.fine_timer(False)
        for path, original in saved_ini.items():
            path.write_bytes(original)
        report['session'] = box.report
        (out / 'summary.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(json.dumps({k: report[k] for k in ('outcome', 'error') if k in report}), flush=True)


if __name__ == '__main__':
    main()
