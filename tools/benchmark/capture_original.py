"""Uncapped original-game render benchmarks in the existing hidden Play Level sandbox.

Run from the repository root. All game-derived captures belong under local/.
The sandbox config and PlayLevel ini are restored even when a capture fails.
"""
from __future__ import annotations

import argparse
import csv
import ctypes
import json
import math
import os
from pathlib import Path
import re
import statistics
import struct
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT / 're/tools/difftest'), str(ROOT / 'tools/golden')]
import sandbox as sb
import livegame
import capture_game as golden


def summary(times):
    ordered = sorted(times)
    tail = max(1, math.ceil(len(times) * .01))
    return dict(frames=len(times), seconds=sum(times) / 1000,
                fps=1000 / statistics.mean(times),
                low1Percent=1000 / statistics.mean(ordered[-tail:]),
                p50Ms=statistics.median(times), p99Ms=ordered[min(len(times)-1, int(len(times)*.99))],
                maxMs=max(times))


def measure(game, reader, seconds):
    # Read the game's own frame delta. Polling timestamps would include Python scheduling noise.
    # Present counter independently detects frames missed by the observer.
    read = game.p.f32
    address = game.ts_addr
    before = reader.presents()
    start = time.perf_counter()
    last = read(address)
    times = []
    deadline = start + seconds
    while time.perf_counter() < deadline:
        value = read(address)
        if value != last:
            times.append((value-last)*1000)
            last = value
    wall = time.perf_counter()-start
    presents = (reader.presents()-before) & 0xffffffff
    return times, dict(wallSeconds=wall, presents=presents, presentFps=presents/wall,
                       observerMissedFrames=max(0, presents-len(times)))


def raise_runtime_cap(game, root):
    """Only the sandbox child's two cap values change; executable code and disk binaries stay intact.

    Build and instruction guards reject other layouts. The cap selector is recorded in REA evidence
    ev_638613ba0a5e2b7a7d2f4debec38febab92d74e06223b7e631187db66961551c.
    """
    digest = sb.sha256(os.path.join(root, 'System', 'Reloaded.Core.dll'))
    if digest != '9840e536d1f98d94176bbeff5a0427dc0684e7bde0464d65ddbd8542fd23feff':
        raise RuntimeError('Uncap requires the verified Reloaded.Core build; use --cap 240 otherwise')
    psapi = ctypes.WinDLL('psapi', use_last_error=True)
    psapi.EnumProcessModulesEx.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32,
                                         ctypes.POINTER(ctypes.c_uint32), ctypes.c_uint32]
    psapi.GetModuleFileNameExW.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_uint32]
    modules = (ctypes.c_void_p * 2048)()
    needed = ctypes.c_uint32()
    if not psapi.EnumProcessModulesEx(game.p.h, modules, ctypes.sizeof(modules), ctypes.byref(needed), 3):
        raise RuntimeError('Cannot enumerate sandbox child modules')
    base = None
    for module in modules[:needed.value // ctypes.sizeof(ctypes.c_void_p)]:
        path = ctypes.create_unicode_buffer(32768)
        psapi.GetModuleFileNameExW(game.p.h, module, path, len(path))
        if os.path.basename(path.value).lower() == 'reloaded.core.dll':
            if sb.sha256(path.value) != digest:
                raise RuntimeError('Loaded module differs from selected sandbox')
            base = module
            break
    if base is None:
        raise RuntimeError('Sandbox child has no Reloaded.Core module')
    address = base + 0x3f272c
    signature = bytes.fromhex('660f6e0d')+struct.pack('<I', address)+bytes.fromhex('eb08660f6e0d')+struct.pack('<I', address+4)
    if game.p.read(base + 0x9e281, len(signature)) != signature:
        raise RuntimeError('Loaded cap selector differs from verified module layout')
    previous = struct.unpack('<2i', game.p.read(address, 8))
    if not all(30 <= value <= 240 for value in previous):
        raise RuntimeError('Unexpected runtime cap values '+str(previous))
    if not game.p.write(address, struct.pack('<2i', 10000, 10000)):
        raise RuntimeError('Could not raise runtime frame cap')
    return dict(previous=previous, runtimeCap=10000, codePatched=False, address=address)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--map', default='SteeE')
    parser.add_argument('--side', default='spy', choices=['spy', 'merc'])
    parser.add_argument('--only')
    parser.add_argument('--spawn-views', action='store_true', help='Four directions at the selected map/side spawn')
    parser.add_argument('--spawn-current', action='store_true', help='The natural spawn camera, without teleporting or turning')
    parser.add_argument('--out', required=True)
    parser.add_argument('--seconds', type=float, default=10)
    parser.add_argument('--warmup', type=float, default=3)
    parser.add_argument('--cap', type=int, default=240)
    parser.add_argument('--uncap', action='store_true', help='Raise only the sandbox child runtime cap to 10000')
    parser.add_argument('--gameplay', action='store_true', help='Include the HUD and local character (use the spawn pose)')
    parser.add_argument('--width', type=int, default=1280)
    parser.add_argument('--height', type=int, default=720)
    parser.add_argument('--sandbox', default=sb.DEFAULT_SANDBOX)
    parser.add_argument('--re-tools', default=sb.DEFAULT_RE_TOOLS)
    args = parser.parse_args()
    if not (Path(args.sandbox) / 'System/ChaosTheory_Editor.exe').is_file():
        parser.error('Sandbox has no ChaosTheory_Editor.exe; select a complete sandbox with --sandbox')
    if args.seconds <= 0 or args.warmup < 0 or not 30 <= args.cap <= 240:
        parser.error('seconds must be positive, warmup nonnegative, and cap between 30 and 240')
    if args.spawn_current:
        if args.spawn_views or args.only: parser.error('--spawn-current cannot be combined with --spawn-views or --only')
        selected = [dict(id='spawn-native', kind='spawn')]
    elif args.spawn_views:
        selected = [dict(id='spawn-'+str(degrees), yaw=degrees*65536//360, kind='spawn')
                    for degrees in (0, 90, 180, 270)]
        if args.only:
            wanted = set(args.only.split(','))
            selected = [p for p in selected if p['id'] in wanted]
            if len(selected) != len(wanted): parser.error('Unknown spawn view')
    else:
        poses = golden.load_poses(ROOT / 'tools/golden/poses' / (args.map+'.json'))
        wanted = set((args.only or 'spy-spawn,shop,courtyard,crane').split(','))
        selected = [p for p in poses['poses'] if p['id'] in wanted]
        if len(selected) != len(wanted):
            parser.error('Some requested poses do not exist')
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    tools = sb.load_tools(args.re_tools)
    ini = Path(args.sandbox) / 'System/Reloaded_PlayLevel.ini'
    original_ini = ini.read_bytes()
    editor_ini = Path(args.sandbox) / 'System/Reloaded_Editor.ini'
    original_editor_ini = editor_ini.read_bytes()
    report = dict(format='svm.fps-benchmark/1', engine='Original (Reloaded Play Level)',
                  map=args.map, side=args.side, binaries=sb.binaries(args.sandbox),
                  secondsPerView=args.seconds, warmupSeconds=args.warmup, cases=[])
    report['gameplay'] = args.gameplay
    cameras = dict(format=golden.FORMAT, map=args.map, team=args.side, shots={})
    livegame.fine_timer(True)
    try:
        box = sb.Sandbox(tools, args.sandbox, fps=0, wait_idle=30)
        box._wait_until_idle()
        editor_text = original_editor_ini.decode('latin-1')
        for key, value in [('ResolutionX', args.width), ('ResolutionY', args.height)]:
            editor_text = re.sub(r'(?m)^'+key+r'=.*$', key+'='+str(value), editor_text)
        editor_ini.write_bytes(editor_text.encode('latin-1'))
        with box:
            tools.headless.frame_cap(args.sandbox, args.cap)
            config = Path(args.sandbox) / 'System/SCCT_Versus.config'
            config_text = config.read_text(encoding='utf-8')
            for key, value in [('shadow_quality', '3.0'), ('resolution_scale', '1.0'), ('filter_level', '4'),
                               ('msaa', 'false'), ('smaa', 'false'), ('shadow_edge_smoothing', 'true'), ('throttle_stop', 'true')]:
                config_text = re.sub(r'("'+key+r'"\s*:\s*)(?:true|false|[0-9.]+)', r'\g<1>'+value, config_text)
            config.write_text(config_text, encoding='utf-8')
            text = original_ini.decode('latin-1')
            for key, value in [('WindowedViewportX', args.width), ('WindowedViewportY', args.height),
                               ('FullscreenViewportX', args.width), ('FullscreenViewportY', args.height),
                               ('UseVSync', 'False')]:
                text = re.sub(r'(?m)^'+key+r'=.*$', key+'='+str(value), text)
            ini.write_bytes(text.encode('latin-1'))
            box.load_map(args.map)
            with box.play(args.side) as (session, info):
                game = livegame.LiveGame(session, delay=0, settle_check=0)
                if args.uncap:
                    cap_info = raise_runtime_cap(game, args.sandbox)
                    cap_address = cap_info.pop('address')
                    report['uncap'] = cap_info
                    game.p.write(cap_address, struct.pack('<2i', args.cap, args.cap))
                camera = golden.Camera(game)
                if not args.gameplay:
                    camera.hide_hud()
                    game.g.set(game.pawn, 'bHidden', 1)
                if args.spawn_views:
                    spawn = camera.read()[1]['Location']
                    for pose in selected:
                        pose['pawn'] = spawn
                report['frameCap'] = tools.headless.frame_cap(args.sandbox)
                reader = tools.headless.FrameReader(info['gamePid'])
                try:
                    for pose in selected:
                        if not args.spawn_current:
                            golden.place(game, camera, pose)
                        time.sleep(args.warmup)
                        golden.settle(game, camera, game.time_seconds())
                        shot = golden.grab(tools, info['gamePid'], game, camera, str(out/(pose['id']+'.png')))
                        if (shot['width'], shot['height']) != (args.width, args.height):
                            raise RuntimeError('Actual frame size differs from requested resolution: '+str(shot))
                        cam, pawn = shot['camera'], shot['pawn']
                        shot['kind'] = pose.get('kind', '')
                        shot['camera'] = dict(location=cam['vCamLocation'], rotation=cam['rCamRotation'],
                                              fov=cam['FovAngle'], behindView=cam.get('bBehindView', 1))
                        shot['pawn'] = dict(location=pawn['Location'], rotation=pawn['Rotation'],
                                            collisionHeight=pawn['CollisionHeight'], prePivot=pawn['PrePivot'])
                        cameras['shots'][pose['id']] = shot
                        if args.uncap:
                            game.p.write(cap_address, struct.pack('<2i', 10000, 10000))
                            time.sleep(0.5)
                        times, observed = measure(game, reader, args.seconds)
                        after_camera, _ = camera.read()
                        observed['cameraDriftUu'] = max(abs(a-b) for a, b in zip(cam['vCamLocation'], after_camera['vCamLocation']))
                        observed['rotationDriftUnits'] = max(abs(a-b) for a, b in zip(cam['rCamRotation'], after_camera['rCamRotation']))
                        shot['endCamera'] = dict(location=after_camera['vCamLocation'], rotation=after_camera['rCamRotation'], fov=after_camera['FovAngle'])
                        if args.uncap:
                            game.p.write(cap_address, struct.pack('<2i', args.cap, args.cap))
                        result = dict(id=pose['id'], width=shot['width'], height=shot['height'],
                                      **summary(times), **observed)
                        report['cases'].append(result)
                        with (out/(pose['id']+'.csv')).open('w', newline='') as f:
                            writer = csv.writer(f)
                            writer.writerow(['frame_ms'])
                            writer.writerows((t,) for t in times)
                        print(json.dumps(result), flush=True)
                        (out/'summary.json').write_text(json.dumps(report, indent=2))
                        (out/'cameras.json').write_text(json.dumps(cameras, indent=2))
                finally:
                    reader.close()
    finally:
        ini.write_bytes(original_ini)
        editor_ini.write_bytes(original_editor_ini)
        livegame.fine_timer(False)


if __name__ == '__main__':
    main()
