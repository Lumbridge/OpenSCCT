"""Capture golden frames of the original game at fixed views, with the camera the game used.

    python -I -B tools/golden/capture_game.py [--poses tools/golden/poses/SteeE.json] [--out local/golden/game]
        [--sandbox F:\\SCCT-Automation\\svm\\Sandbox] [--re-tools <RE+ tools/automation>] [--only id,id]

One headless Play Level of the pose file's map in the project sandbox, under the play lock (the same
path as the difftest recorder: re/tools/difftest/sandbox.py). For each pose the pawn is teleported
and the controller's view rotation written (memory writes on the local player only; Physics is never
written), the HUD is hidden through HUD.bHideHUD, the tool waits until the pawn and the camera are
still, then saves the next frame the game presents (the RE+ headless d3d9 frame share, the same
capture as `headless.py frame`) to <out>/<Map>/<pose>.png.

<out>/<Map>/cameras.json records, per pose, the camera the frame was drawn with:
SPlayerController.vCamLocation and rCamRotation, the controller Rotation, FovAngle and DefaultFOV,
the pawn's Location, Rotation, zone and animation channels, LevelInfo.TimeSeconds and the frame size. The camera is read
before and after the grab; a pose whose two reads differ is retried.

With --rendmap the local player's view is put in that vision mode before every grab, the way the goggles leave it
(SGoggle.CurrentMode and PlayerController.RendMap set to the mode, the pawn's bGogglesUp cleared; memory writes on
the local player only), and the frames go to local/golden/game-vision/<mode>/<Map>/ unless --out says otherwise.
Night (34) and thermal (36) are the spy's modes, motion (37) and EMF (38) the merc's: --team defaults to the mode's
team. Without a real goggle key press the motion tracker actor does not exist, so motion frames carry no lock
brackets (the HUD is hidden anyway).

Frames and camera records are game-derived data: they stay under local/ (gitignored).
"""
from __future__ import annotations

import argparse
import json
import os
import struct
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
DIFFTEST = os.path.join(REPO, 're', 'tools', 'difftest')
sys.path.insert(0, DIFFTEST)

import livegame  # noqa: E402
import sandbox as sb  # noqa: E402

FORMAT = 'svm.golden-cameras/1'
CLEARANCE = 2.0           # uu above the floor for a pawn put on a 'floor' point (as the difftest recorder)
STILL_SPEED = 0.5         # uu/s
CAMERA_EPS = 0.05         # uu: camera location change still counted as still
STABLE_FRAMES = 30        # frames the pawn and camera must stay still before a grab
MAX_SETTLE_FRAMES = 420   # 7 s at 60 fps
GRAB_TRIES = 5

CAMERA_FIELDS = {'vCamLocation': 'vec', 'rCamRotation': 'rot', 'Rotation': 'rot', 'FovAngle': 'float',
                 'DefaultFOV': 'float', 'myHUD': 'ptr', 'bBehindView': 'bool', 'RendMap': 'int', 'DoGlow': 'int'}
PAWN_FIELDS = {'Location': 'vec', 'Rotation': 'rot', 'Velocity': 'vec', 'CollisionHeight': 'float',
               'Physics': 'byte', 'PrePivot': 'vec', 'MeshInstance': 'ptr', 'bIsCrouched': 'bool',
               'HeatIntensity': 'float', 'HeatOpacity': 'float', 'EMFIntensity': 'float', 'EMFOpacity': 'float'}


def log(*parts):
    sb.log('[golden]', *parts)


def load_poses(path):
    with open(path, encoding='utf-8') as f:
        doc = json.load(f)
    if doc.get('format') != 'svm.golden-poses/1':
        raise SystemExit('%s: not an svm.golden-poses/1 file' % path)
    ids = [p['id'] for p in doc['poses']]
    if len(set(ids)) != len(ids):
        raise SystemExit('%s: duplicate pose ids' % path)
    return doc


class Camera:
    """The local controller's camera fields and the pawn's placement, read from memory."""

    def __init__(self, game):
        self.game = game
        self.cam = livegame.Layout(game.g, game.pc, CAMERA_FIELDS)
        self.pawn = livegame.Layout(game.g, game.pawn, PAWN_FIELDS)
        props = game.g.props(game.g.class_of(game.pawn))
        self.region_offset = props['Region'][0] if 'Region' in props else None
        if self.cam.missing:
            log('controller fields not found:', self.cam.missing)

    def _block(self, obj, layout):
        data = self.game.p.read(obj + layout.lo, layout.hi - layout.lo)
        return layout.decode(data) if len(data) == layout.hi - layout.lo else None

    def read(self):
        cam = self._block(self.game.pc, self.cam)
        pawn = self._block(self.game.pawn, self.pawn)
        return cam, pawn

    def anim(self, pawn):
        """The pawn's animation channels: [channel, sequence, frame 0..1, rate, blend alpha]."""
        mi = pawn.get('MeshInstance') if pawn else 0
        channels, _ = self.game._channels(mi)
        return [[c[0], c[1], round(c[2], 5), round(c[3], 4), round(c[4], 4)] for c in channels]

    def zone(self):
        if self.region_offset is None:
            return None
        ptr = self.game._u32(self.game.pawn + self.region_offset)
        return self.game.g.r.obj_name(ptr) if ptr else None

    def hide_hud(self):
        cam, _ = self.read()
        hud = cam.get('myHUD') if cam else 0
        if not hud:
            return {'hud': None}
        g = self.game.g
        try:
            g.set(hud, 'bHideHUD', 1)
            return {'hud': g.r.class_name(hud), 'bHideHUD': g.get(hud, 'bHideHUD')}
        except (KeyError, TypeError) as error:
            return {'hud': g.r.class_name(hud), 'error': str(error)}


VISION_TEAM = {34: 'spy', 36: 'spy', 37: 'merc', 38: 'merc'}


def pointer(value):
    """An object reference as read by the live layer (an int, or text such as '0x1234: Name')."""
    if isinstance(value, int):
        return value
    if not value or value == 'None':
        return 0
    try:
        return int(str(value).split(':')[0], 16)
    except ValueError:
        return 0


def set_vision(game, mode):
    """Puts the local view in vision mode `mode` (SGoggle.CurrentMode, PlayerController.RendMap, bGogglesUp)."""
    g = game.g
    goggle = pointer(g.get(game.pc, 'Goggle'))
    out = {'goggle': bool(goggle)}
    if goggle:
        g.set(goggle, 'CurrentMode', mode)
    g.set(game.pc, 'RendMap', mode)
    try:
        g.set(game.pawn, 'bGogglesUp', 0 if mode != 5 else 1)
    except (KeyError, TypeError) as error:
        out['bGogglesUp'] = str(error)
    out['rendMap'] = g.get(game.pc, 'RendMap')
    return out


def same_camera(a, b):
    if a is None or b is None:
        return False
    la, lb = a['vCamLocation'], b['vCamLocation']
    return (max(abs(x - y) for x, y in zip(la, lb)) <= CAMERA_EPS and a['rCamRotation'] == b['rCamRotation']
            and a['FovAngle'] == b['FovAngle'])


def settle(game, camera, prev_t):
    """Frames until the pawn is still and the camera unchanged for STABLE_FRAMES frames."""
    stable = 0
    last = None
    frames = 0
    t = prev_t
    while frames < MAX_SETTLE_FRAMES:
        t, _ = game.wait_frame(t)
        frames += 1
        cam, pawn = camera.read()
        if cam is None or pawn is None:
            continue
        v = pawn['Velocity']
        moving = livegame.horizontal_speed(v) > STILL_SPEED or abs(v[2]) > STILL_SPEED
        if not moving and same_camera(cam, last):
            stable += 1
            if stable >= STABLE_FRAMES:
                return t, {'frames': frames}
        else:
            stable = 0
        last = cam
    return t, {'frames': frames, 'timedOut': True}


def place(game, camera, pose):
    _, pawn = camera.read()
    if 'pawn' in pose:
        loc = [float(v) for v in pose['pawn']]
    else:
        x, y, z = pose['floor']
        loc = [float(x), float(y), float(z) + float(pawn['CollisionHeight']) + CLEARANCE]
    game.teleport(loc, pose['yaw'])
    game.set_view(pose.get('pitch', 0), pose['yaw'])
    return loc


def grab(tools, pid, game, camera, path):
    reader = tools.headless.FrameReader(pid)
    try:
        for attempt in range(1, GRAB_TRIES + 1):
            before, pawn_before = camera.read()
            t_before = game.time_seconds()
            width, height, data = reader.grab(timeout=10.0)
            after, pawn_after = camera.read()
            if same_camera(before, after):
                from PIL import Image
                image = Image.frombuffer('RGB', (width, height), data, 'raw', 'BGRX', 0, 1)
                os.makedirs(os.path.dirname(path), exist_ok=True)
                image.save(path)
                return {'image': os.path.basename(path), 'width': width, 'height': height, 'tries': attempt,
                        'camera': before, 'pawn': pawn_after, 'timeSeconds': t_before}
            log('camera moved during the grab, retrying')
            game.wait_frame(game.time_seconds())
        raise RuntimeError('the camera did not hold still over a grab in %d tries' % GRAB_TRIES)
    finally:
        reader.close()


def capture(args):
    doc = load_poses(args.poses)
    map_name = doc['map']
    poses = doc['poses']
    if args.only:
        wanted = set(args.only.split(','))
        poses = [p for p in poses if p['id'] in wanted]
    if args.rendmap and not args.out:
        args.out = os.path.join(REPO, 'local', 'golden', 'game-vision', str(args.rendmap))
    out = os.path.join(os.path.abspath(args.out or os.path.join(REPO, 'local', 'golden', 'game')), map_name)
    team = args.team or VISION_TEAM.get(args.rendmap) or doc.get('team', 'spy')
    os.makedirs(out, exist_ok=True)
    tools = sb.load_tools(args.re_tools)
    root = os.path.abspath(args.sandbox)
    record = {'format': FORMAT, 'map': map_name, 'team': team, 'rendMap': args.rendmap or 5,
              'poses': os.path.relpath(os.path.abspath(args.poses), REPO).replace('\\', '/'),
              'binaries': sb.binaries(root), 'started': time.strftime('%Y-%m-%dT%H:%M:%S'), 'shots': {}}
    cameras_path = os.path.join(out, 'cameras.json')
    if os.path.isfile(cameras_path) and args.only:
        with open(cameras_path, encoding='utf-8') as f:
            record['shots'] = json.load(f).get('shots', {})
    livegame.fine_timer(True)
    try:
        with sb.Sandbox(tools, root, fps=args.fps) as box:
            record['mapFile'] = box.load_map(map_name)
            with box.play(record['team']) as (session, info):
                record['game'] = {k: v for k, v in info.items() if k in ('side', 'gameUpSeconds', 'pawnSeconds', 'gamePid')}
                game = livegame.LiveGame(session)
                camera = Camera(game)
                record['hud'] = camera.hide_hud()
                log('hud', record['hud'])
                if args.rendmap:
                    record['vision'] = set_vision(game, args.rendmap)
                    log('vision', record['vision'])
                t = game.time_seconds()
                for pose in poses:
                    loc = place(game, camera, pose)
                    if args.rendmap:
                        set_vision(game, args.rendmap)
                    t, still = settle(game, camera, t)
                    # The HUD can be recreated on respawn; hide it again right before the grab.
                    camera.hide_hud()
                    path = os.path.join(out, pose['id'] + '.png')
                    shot = grab(tools, info['gamePid'], game, camera, path)
                    cam = shot['camera']
                    record['shots'][pose['id']] = {
                        'kind': pose.get('kind'), 'image': shot['image'], 'width': shot['width'],
                        'height': shot['height'], 'teleport': loc, 'settle': still, 'zone': camera.zone(),
                        'camera': {'location': cam['vCamLocation'], 'rotation': cam['rCamRotation'],
                                   'controllerRotation': cam['Rotation'], 'fov': cam['FovAngle'],
                                   'defaultFov': cam['DefaultFOV'], 'behindView': cam.get('bBehindView'),
                                   'rendMap': cam.get('RendMap'), 'doGlow': cam.get('DoGlow')},
                        'pawn': {'location': shot['pawn']['Location'], 'rotation': shot['pawn']['Rotation'],
                                 'prePivot': shot['pawn'].get('PrePivot'),
                                 'collisionHeight': shot['pawn']['CollisionHeight'],
                                 'crouched': shot['pawn'].get('bIsCrouched'), 'anim': camera.anim(shot['pawn']),
                                 'vision': {k: shot['pawn'].get(k) for k in ('HeatIntensity', 'HeatOpacity', 'EMFIntensity',
                                                                             'EMFOpacity')}},
                        'timeSeconds': shot['timeSeconds'], 'grabTries': shot['tries']}
                    log(pose['id'], 'saved', shot['width'], 'x', shot['height'], 'zone', record['shots'][pose['id']]['zone'],
                        'cam', [round(v, 1) for v in cam['vCamLocation']], cam['rCamRotation'], 'fov', cam['FovAngle'])
                    t = game.time_seconds()
        record['sandbox'] = box.report
    finally:
        livegame.fine_timer(False)
        record['finished'] = time.strftime('%Y-%m-%dT%H:%M:%S')
        with open(cameras_path, 'w', encoding='utf-8') as f:
            json.dump(record, f, indent=1, default=str)
    log('wrote', cameras_path)
    return record


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('--poses', default=os.path.join(HERE, 'poses', 'SteeE.json'))
    parser.add_argument('--out', default=None, help='default local/golden/game, or local/golden/game-vision/<mode>')
    parser.add_argument('--sandbox', default=sb.DEFAULT_SANDBOX)
    parser.add_argument('--re-tools', default=os.environ.get('SVM_RE_TOOLS', sb.DEFAULT_RE_TOOLS))
    parser.add_argument('--fps', type=int, default=60, help="the game's frame cap for the run (restored after)")
    parser.add_argument('--only', help='comma-separated pose ids')
    parser.add_argument('--rendmap', type=int, choices=(34, 36, 37, 38), help='vision mode of the frames')
    parser.add_argument('--team', choices=('spy', 'merc'), help="the player's side (default: the mode's, else the pose file's)")
    args = parser.parse_args()
    capture(args)


if __name__ == '__main__':
    main()
