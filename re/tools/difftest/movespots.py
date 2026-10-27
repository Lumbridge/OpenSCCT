"""Find test spots for the rest of the moveset (ladders, hand over hand, fences, barrier vaults, wall jumps, split
jump corridors, steps, slopes and drops) with the real engine's collision, through the sandbox editor's live API.

    python -I -B re/tools/difftest/movespots.py --map MounE --out local/difftest/spots/MounE-moves.json \\
        [--floors local/difftest/spots/MounE.floors.json] [--bounds x0,y0,x1,y1] [--only ladder,hoh,...]

The editor runs headless in the sandbox; no game is started, so the play lock is not taken. The GE list is the
decoder's output (local/formats/ge/<Map>.json). The result is game-derived data and stays under local/.

Parts (`--only`, default all):
- ladder: per LADDER GE, the floor at its foot on the climber's side (the side point's direction), a standing
  box swept toward it, the floor at the top exit (beyond the ladder) and whether it is flush, and a lane on the
  top floor toward the top for getting on from above.
- hoh: per HOH GE, the floor under it along its length, its height over that floor, and a standing lane under
  it along the pipe.
- fence: per FENCE GE and side, the floor at 40 to 150 out (the fence height over it), the texture a line
  trace hits on the fence face, and a standing box swept toward the fence.
- barrier: LG sides from gespots.survey_lg whose ledge is 80 to 130 over a flat floor with the wall under it,
  and the CheckBarrier room test (a (35, 35, 22.5) box over the top 140 beyond and down 85.2).
- walljump: LG sides with a wall under the lip over a flat floor where the ledge is 130 to 335 over a standing
  spy's Location (the wall jump window), with the run-up floor and the CheckGEForWallJump wall traces (at the
  pawn's height, 160 higher and 35 to each side) from a spot 300 out.
- split: walkable flat floor cells in corridors whose width (line traces both ways at the heights the split
  jump probes) is 182 to 200, grouped into corridors.
- steps: edges between flat walkable floors 4 to 64 apart (a step up), grouped, and runs of steps (stairs).
- slopes: walkable floor patches with a normal Z from 0.7 to 0.99 (ramps), with the gradient direction.
- drops: walk-off edges over a flat walkable floor 200 to 700 below (spots.drop_edges).

split, steps, slopes and drops need a floor map: --floors reads one written by spots.py (or by this tool), else
it is built over --bounds (default: the GE bounds of the map grown by 1500) and cached next to --out.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import gespots  # noqa: E402
import sandbox as sb  # noqa: E402
import spots  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))

STAND = (35.0, 35.0, 87.5)
HOVER = 2.0
SPY_LOC_OVER_FLOOR = 88.5          # R4: Location.Z = floor + CollisionHeight 87.5 + 1.0 while walking
JUMP_APEX = 124.0
WALLJUMP_WINDOW = (130.0, 335.0)   # gz - Location.Z (s_PlayerWalkingMario wall jump test)
BARRIER_DROP = (80.0, 130.0)
SPLIT_WIDTH = (182.0, 200.0)       # CheckWallJump, nJumpNumber 1: d1 + d2 in [182, 200)
VANDAMME_WIDTH = (180.0, 198.0)    # nJumpNumber 2
PARTS = ('ladder', 'hoh', 'fence', 'barrier', 'walljump', 'split', 'steps', 'slopes', 'drops')


def r1(v):
    return [round(float(x), 1) for x in v]


def floor_under(live, pts, reach=1200):
    return gespots.floors(live, pts, reach=reach)


# ---- ladders --------------------------------------------------------------------------------------

def survey_ladders(live, ges):
    out = []
    for g in ges:
        if g['kind'] != 'LADDER':
            continue
        b, t, side = g['points']
        n = gespots.unit2d(side[0] - b[0], side[1] - b[1])
        foot_d = (40.0, 60.0, 100.0, 150.0, 200.0)
        top_d = (40.0, 60.0, 80.0, 120.0, 160.0, 240.0)
        foot = [gespots.offset(b, n, d, b[2] + 60.0) for d in foot_d]
        top = [gespots.offset(t, n, -d, t[2] + 60.0) for d in top_d]
        fl = floor_under(live, foot + top, reach=600)
        ff, ft = fl[:len(foot_d)], fl[len(foot_d):]
        e = {'ge': g['index'], 'id': g['id'], 'active': g['active'], 'bottom': r1(b), 'top': r1(t),
             'height': round(t[2] - b[2], 1), 'toClimber': [round(n[0], 4), round(n[1], 4)],
             'faceYaw': gespots.yaw_of(-n[0], -n[1]),
             'footFloors': [[d, None if f is None else round(f[0], 2), None if f is None else round(f[1], 3)]
                            for d, f in zip(foot_d, ff)],
             'topFloors': [[d, None if f is None else round(f[0], 2), None if f is None else round(f[1], 3)]
                           for d, f in zip(top_d, ft)]}
        zf = ff[2][0] if ff[2] else None
        if zf is not None:
            zc = zf + STAND[2] + HOVER
            start = gespots.offset(b, n, 200.0, zc)
            r = gespots.sweeps(live, [[start, gespots.offset(b, n, 10.0, zc)]], STAND)[0]
            e['floorZ'] = round(zf, 2)
            e['start200'] = [round(start[0], 1), round(start[1], 1), round(zf, 2)]
            if r.get('startInside'):
                e['lane'] = {'startInside': True}
            elif r.get('hit'):
                p = [float(v) for v in r['point']]
                e['lane'] = {'hit': True, 'stopsAt': round((p[0] - b[0]) * n[0] + (p[1] - b[1]) * n[1], 1),
                             'actor': r.get('actor'), 'bsp': bool(r.get('bsp'))}
            else:
                e['lane'] = {'hit': False}
        tz = [f[0] for f in ft[1:4] if f is not None]
        if tz:
            e['topFloorZ'] = round(tz[0], 2)
            e['topFlush'] = -80.0 <= tz[0] - t[2] <= 8.0
            zc = tz[0] + STAND[2] + HOVER
            a = gespots.offset(t, n, -260.0, zc)
            r = gespots.sweeps(live, [[a, gespots.offset(t, n, -20.0, zc)]], STAND)[0]
            e['topLane'] = {'startInside': bool(r.get('startInside')), 'hit': bool(r.get('hit')),
                            'stopsAt': None if not r.get('hit') else round(
                                -((float(r['point'][0]) - t[0]) * n[0] + (float(r['point'][1]) - t[1]) * n[1]), 1),
                            'start260': [round(a[0], 1), round(a[1], 1), round(tz[0], 2)],
                            'faceYaw': gespots.yaw_of(n[0], n[1])}
        out.append(e)
    return out


# ---- hand over hand -------------------------------------------------------------------------------

def survey_hoh(live, ges):
    out = []
    for g in ges:
        if g['kind'] != 'HOH':
            continue
        p1, p2 = g['points']
        u = gespots.unit2d(p2[0] - p1[0], p2[1] - p1[1])
        length = math.hypot(p2[0] - p1[0], p2[1] - p1[1])
        fr = [k / 10.0 for k in range(1, 10)]
        pts = [[p1[0] + (p2[0] - p1[0]) * f, p1[1] + (p2[1] - p1[1]) * f, p1[2] + (p2[2] - p1[2]) * f - 20.0]
               for f in fr]
        fl = floor_under(live, pts, reach=1500)
        heights = [None if f is None else round(p[2] + 20.0 - f[0], 1) for p, f in zip(pts, fl)]
        e = {'ge': g['index'], 'id': g['id'], 'active': g['active'], 'p1': r1(p1), 'p2': r1(p2),
             'length': round(length, 1), 'yawP1toP2': gespots.yaw_of(u[0], u[1]),
             'floors': [None if f is None else round(f[0], 2) for f in fl], 'heights': heights}
        zs = [f[0] for f in fl if f is not None]
        if zs and len(zs) == len(fl) and max(zs) - min(zs) <= 2.0:
            zf = zs[0]
            e['floorZ'] = round(zf, 2)
            e['flatUnder'] = True
            zc = zf + STAND[2] + HOVER
            a = [p1[0] + u[0] * 40, p1[1] + u[1] * 40, zc]
            bpt = [p2[0] - u[0] * 40, p2[1] - u[1] * 40, zc]
            r = gespots.sweeps(live, [[a, bpt]], STAND)[0]
            e['laneUnder'] = {'startInside': bool(r.get('startInside')), 'hit': bool(r.get('hit')),
                              'dist': round(float(r['dist']), 1) if r.get('hit') else round(length - 80, 1)}
        out.append(e)
    return out


# ---- fences ---------------------------------------------------------------------------------------

def survey_fences(live, ges):
    out = []
    for g in ges:
        if g['kind'] != 'FENCE':
            continue
        mid, u, length, n = gespots.lg_frame(g['points'])
        for sign in (1, -1):
            nn = (n[0] * sign, n[1] * sign)
            ds = (40.0, 60.0, 100.0, 150.0)
            fl = floor_under(live, [gespots.offset(mid, nn, d, mid[2] + 20.0) for d in ds], reach=1500)
            s = {'ge': g['index'], 'id': g['id'], 'active': g['active'], 'sign': sign, 'mid': r1(mid),
                 'p1': r1(g['points'][0]), 'p2': r1(g['points'][1]), 'length': round(length, 1),
                 'normal': [round(nn[0], 4), round(nn[1], 4)], 'faceYaw': gespots.yaw_of(-nn[0], -nn[1]),
                 'floors': [[d, None if f is None else round(f[0], 2)] for d, f in zip(ds, fl)]}
            zf = fl[1][0] if fl[1] else None
            if zf is not None:
                s['floorZ'] = round(zf, 2)
                s['height'] = round(mid[2] - zf, 1)
                hz = zf + (mid[2] - zf) * 0.5
                res = live.trace([[gespots.offset(mid, nn, 80.0, hz), gespots.offset(mid, nn, -80.0, hz)],
                                  [gespots.offset(mid, nn, 80.0, zf + 60), gespots.offset(mid, nn, -80.0, zf + 60)]],
                                 texture=True)
                s['face'] = [{'hit': bool(r.get('hit')), 'dist': round(float(r.get('dist', 0)), 1) if r.get('hit')
                              else None, 'texture': r.get('texture'), 'actor': r.get('actor'),
                              'bsp': bool(r.get('bsp'))} for r in res]
                zc = zf + STAND[2] + HOVER
                start = gespots.offset(mid, nn, 200.0, zc)
                r = gespots.sweeps(live, [[start, gespots.offset(mid, nn, 0.0, zc)]], STAND)[0]
                s['lane'] = {'startInside': bool(r.get('startInside')), 'hit': bool(r.get('hit')),
                             'stopsAt': round(float(r['dist']), 1) if r.get('hit') else None,
                             'start200': [round(start[0], 1), round(start[1], 1), round(zf, 2)]}
            out.append(s)
    return out


# ---- barrier and wall jump (from the LG survey) ---------------------------------------------------

def barrier_spots(live, sides):
    low = [s for s in sides if 'drop' in s and (s.get('wall') or {}).get('hit') and s.get('flat')
           and BARRIER_DROP[0] <= s['drop'] <= BARRIER_DROP[1] and 28.0 <= s['wall']['fromLine'] <= 45.0]
    out, checks = [], []
    for s in low:
        nn = s['normal']
        top = s['mid'][2]
        stand = gespots.offset(s['mid'], nn, 36.0, s['floorZ'])
        a = [stand[0], stand[1], top + 23.5]
        over = gespots.offset(s['mid'], nn, -140.0, top + 23.5)
        down = [over[0], over[1], top + 23.5 - 85.2]
        checks.append((s, [[a, over], [over, down]]))
        far = gespots.offset(s['mid'], nn, -180.0, top + 20.0)
        s['_far'] = far
    res = gespots.sweeps(live, [t for _, ts in checks for t in ts], (35.0, 35.0, 22.5))
    fl = floor_under(live, [s['_far'] for s in low], reach=1500)
    for k, (s, _) in enumerate(checks):
        r_over, r_down = res[2 * k], res[2 * k + 1]
        clear = not any(r.get('hit') or r.get('startInside') for r in (r_over, r_down))
        f = fl[k]
        out.append({'ge': s['ge'], 'length': s['length'], 'drop': s['drop'], 'floorZ': s['floorZ'],
                    'wallFromLine': s['wall']['fromLine'], 'normal': s['normal'],
                    'faceYaw': gespots.yaw_of(-s['normal'][0], -s['normal'][1]),
                    'stand100': [round(v, 1) for v in gespots.offset(s['mid'], s['normal'], 100.0, s['floorZ'])],
                    'roomOver': clear, 'farFloorZ': None if f is None else round(f[0], 2),
                    'corners': s['corners'], 'active': s['active']})
    for s in low:
        s.pop('_far', None)
    out.sort(key=lambda c: (not c['roomOver'], c['farFloorZ'] is None or abs(c['farFloorZ'] - c['floorZ']) > 4,
                            -c['length']))
    return out


def walljump_spots(live, sides, run=600.0):
    cands = []
    for s in sides:
        w = s.get('wall') or {}
        if 'drop' not in s or not w.get('hit') or not s.get('flat') or not 28.0 <= w['fromLine'] <= 45.0:
            continue
        d = s['drop'] - SPY_LOC_OVER_FLOOR
        if not WALLJUMP_WINDOW[0] < d < WALLJUMP_WINDOW[1]:
            continue
        cands.append((s, d))
    out = []
    for s, d in cands:
        nn = s['normal']
        zf = s['floorZ']
        ds = [float(k) for k in range(64, int(run) + 1, 32)]
        fl = floor_under(live, [gespots.offset(s['mid'], nn, k, zf + 60.0) for k in ds], reach=200)
        flat = 0.0
        for k, f in zip(ds, fl):
            if f is None or abs(f[0] - zf) > 1.0 or f[1] < 0.999:
                break
            flat = k
        zc = zf + STAND[2] + HOVER
        sw = gespots.sweeps(live, [[gespots.offset(s['mid'], nn, max(flat, 64.0), zc),
                                    gespots.offset(s['mid'], nn, 40.0, zc)]], STAND)[0]
        # CheckGEForWallJump walls: from 300 out, line traces toward the wall at the pawn's height, 160 higher, and
        # 35 to each side; they must hit a static surface facing the pawn at consistent distances.
        loc_z = zf + SPY_LOC_OVER_FLOOR
        side = gespots.left_normal(nn)
        starts = [(0.0, loc_z), (0.0, loc_z + 160.0), (35.0, loc_z), (-35.0, loc_z)]
        tr = []
        for o, z in starts:
            p = gespots.offset(s['mid'], nn, 300.0, z)
            p = [p[0] + side[0] * o, p[1] + side[1] * o, z]
            tr.append([p, [p[0] - nn[0] * 500.0, p[1] - nn[1] * 500.0, z]])
        res = live.trace(tr)
        walls = [{'hit': bool(r.get('hit')), 'dist': round(float(r['dist']), 1) if r.get('hit') else None,
                  'facing': round(-float(r['normal'][0]) * nn[0] - float(r['normal'][1]) * nn[1], 3)
                  if r.get('hit') and r.get('normal') else None, 'actor': r.get('actor'), 'bsp': bool(r.get('bsp'))}
                 for r in res]
        kick = 'up' if d > 175 else ('upSmall' if d > 150 else 'waist')
        out.append({'ge': s['ge'], 'length': s['length'], 'drop': s['drop'], 'floorZ': zf, 'gzOverLocation': round(d, 1),
                    'kick': kick, 'wallFromLine': s['wall']['fromLine'], 'normal': nn,
                    'faceYaw': gespots.yaw_of(-nn[0], -nn[1]), 'runUpFlat': flat,
                    'runUpClear': not (sw.get('hit') or sw.get('startInside')),
                    'start': [round(v, 1) for v in gespots.offset(s['mid'], nn, min(flat, 500.0), zf)],
                    'walls300': walls, 'corners': s['corners'], 'active': s['active'],
                    'pullUp': None})
    out.sort(key=lambda c: (not c['runUpClear'], -min(c['runUpFlat'], 400), abs(c['gzOverLocation'] - 250)))
    return out


# ---- floor map parts --------------------------------------------------------------------------------

def load_floor_map(path):
    with open(path, encoding='utf-8') as f:
        c = json.load(f)
    fmap = {tuple(int(v) for v in k.split(',')): [tuple(e) for e in v] for k, v in c['floors'].items()}
    return fmap, tuple(c['grid'])


def build_floor_map(live, ge_doc, bounds, step, cache):
    zb = ge_doc['bounds']
    zlo, zhi = zb[0][2] - 600.0, zb[1][2] + 600.0
    levels = []
    z = zhi
    while z >= zlo:
        levels.append(round(z, 1))
        z -= 110.0
    fmap, grid = spots.floor_map(live, bounds, step, levels)
    with open(cache, 'w', encoding='utf-8') as f:
        json.dump({'grid': list(grid), 'floors': {'%d,%d' % k: v for k, v in fmap.items()}, 'levels': levels}, f)
    return fmap, grid


def region_of(live, fmap, grid):
    starts = spots.player_starts(live)
    seeds = spots.seed_floors(fmap, grid, starts)
    return spots.walkable(fmap, grid, seeds), starts


def split_corridors(live, fmap, grid, region, top=40):
    """Cells whose corridor width (wall to wall through the cell) is in the split-jump window at the heights the
    natives probe: at the apex of a standing jump (Location + 118, about floor + 330) and 160 higher (the second
    probe runs along the pawn's right axis from 0.4 H below Location after the push)."""
    x0, y0, step, nx, ny = grid
    cells = []
    for i, j, z in region:
        nz = next((e[1] for e in fmap.get((i, j), []) if abs(e[0] - z) <= 0.5), 0.0)
        if nz >= 0.999:
            cells.append((i, j, z))
    heights = (330.0, 250.0, 420.0)
    dirs = ((1, 0), (0, 1))
    traces, meta = [], []
    for c in cells:
        x, y = x0 + c[0] * step, y0 + c[1] * step
        for h in heights:
            for dx, dy in dirs:
                for sgn in (1, -1):
                    p = [x, y, c[2] + h]
                    traces.append([p, [x + sgn * dx * 320.0, y + sgn * dy * 320.0, c[2] + h]])
                    meta.append((c, h, (dx, dy), sgn))
    dist = {}
    t = time.time()
    for k in range(0, len(traces), 5000):
        res = live.trace(traces[k:k + 5000])
        for m, r in zip(meta[k:k + 5000], res):
            dist[m] = float(r['dist']) if r.get('hit') and not r.get('startInside') else None
    sb.log('split: %d cells, %d traces in %.1fs' % (len(cells), len(traces), time.time() - t))
    hits = []
    for c in cells:
        for axis in dirs:
            widths = {}
            for h in heights:
                a, b = dist.get((c, h, axis, 1)), dist.get((c, h, axis, -1))
                widths[h] = None if a is None or b is None else round(a + b, 1)
            w = widths[330.0]
            if w is not None and SPLIT_WIDTH[0] <= w < SPLIT_WIDTH[1]:
                hits.append({'cell': c, 'axis': axis, 'widths': widths,
                             'tall': all(v is not None and abs(v - w) <= 6 for v in widths.values())})
    # Group by axis and adjacency along the corridor.
    groups = []
    by = {}
    for hgt in hits:
        by.setdefault((hgt['axis'], hgt['cell'][0], hgt['cell'][1]), hgt)
    left = set(by)
    while left:
        k = left.pop()
        grp, todo = [by[k]], [k]
        while todo:
            ax, i, j = todo.pop()
            for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                k2 = (ax, i + di, j + dj)
                if k2 in left and abs(by[k2]['cell'][2] - by[(ax, i, j)]['cell'][2]) <= 2:
                    left.discard(k2)
                    grp.append(by[k2])
                    todo.append(k2)
        groups.append(grp)
    out = []
    for grp in groups:
        ax = grp[0]['axis']
        xs = [x0 + h['cell'][0] * step for h in grp]
        ys = [y0 + h['cell'][1] * step for h in grp]
        tall = sum(1 for h in grp if h['tall'])
        out.append({'axis': 'x' if ax == (1, 0) else 'y', 'cells': len(grp), 'tall': tall,
                    'box': [[min(xs), min(ys)], [max(xs), max(ys)]], 'floorZ': round(grp[0]['cell'][2], 2),
                    'widths': sorted({h['widths'][330.0] for h in grp})[:6],
                    'sample': [[x0 + h['cell'][0] * step, y0 + h['cell'][1] * step, round(h['cell'][2], 2),
                                h['widths']] for h in grp[:6]]})
    out.sort(key=lambda g: (-g['tall'], -g['cells']))
    return out[:top]


def step_edges(fmap, grid, region, top=60):
    """Edges between flat walkable floors 4 to 64 apart (4-neighbour cells), grouped along the edge; and stairs:
    runs of at least three rises of 4 to 34 in one direction within 160 uu each."""
    x0, y0, step, nx, ny = grid
    walk = {}
    for i, j, z in region:
        walk.setdefault((i, j), []).append(z)

    def flat(i, j, z):
        return any(abs(e[0] - z) <= 0.5 and e[1] >= 0.999 for e in fmap.get((i, j), []))
    edges = []
    for i, j, z in region:
        if not flat(i, j, z):
            continue
        for di, dj in ((1, 0), (0, 1), (-1, 0), (0, -1)):
            for z2 in walk.get((i + di, j + dj), []) + [e[0] for e in fmap.get((i + di, j + dj), [])]:
                rise = z2 - z
                if 4.0 < rise <= 64.0 and flat(i + di, j + dj, z2):
                    edges.append((i, j, z, di, dj, round(rise, 1), z2))
    edges = sorted(set(edges))
    groups = {}
    for e in edges:
        key = (e[3], e[4], e[5], round(e[2], 1))
        groups.setdefault(key, []).append(e)
    out = []
    for (di, dj, rise, z), es in groups.items():
        xs = [x0 + e[0] * step for e in es]
        ys = [y0 + e[1] * step for e in es]
        reachable_top = any((e[0] + di, e[1] + dj, e[6]) in region for e in es)
        out.append({'rise': rise, 'lowZ': z, 'highZ': round(z + rise, 2), 'up': [di, dj],
                    'upYaw': gespots.yaw_of(di, dj), 'cells': len(es), 'topWalkable': reachable_top,
                    'box': [[min(xs), min(ys)], [max(xs), max(ys)]],
                    'sample': [[x0 + e[0] * step, y0 + e[1] * step] for e in es[:4]]})
    out.sort(key=lambda g: -g['cells'])
    stairs = []
    by_cell = {(e[0], e[1], round(e[2], 1), e[3], e[4]): e for e in edges}
    for e in edges:
        i, j, z, di, dj, rise, z2 = e
        if rise > 34.0:
            continue
        run = [e]
        ci, cj, cz = i + di, j + dj, z2
        for _ in range(40):
            nxt = None
            for k in range(0, 6):
                cand = by_cell.get((ci + di * k, cj + dj * k, round(cz, 1), di, dj))
                if cand and cand[5] <= 34.0:
                    nxt = cand
                    break
            if not nxt:
                break
            run.append(nxt)
            ci, cj, cz = nxt[0] + di, nxt[1] + dj, nxt[6]
        if len(run) >= 3:
            stairs.append({'start': [x0 + i * step, y0 + j * step, z], 'upYaw': gespots.yaw_of(di, dj),
                           'rises': [r[5] for r in run], 'treads': [
                               round(abs((run[k + 1][0] - run[k][0]) * di + (run[k + 1][1] - run[k][1]) * dj) * step, 1)
                               for k in range(len(run) - 1)], 'topZ': round(run[-1][6], 2)})
    stairs.sort(key=lambda s: -len(s['rises']))
    seen, uniq = set(), []
    for s in stairs:
        key = (s['upYaw'], round(s['topZ']), tuple(s['rises']))
        if key in seen:
            continue
        seen.add(key)
        uniq.append(s)
    return out[:top], uniq[:top]


def slope_patches(fmap, grid, region, top=30):
    x0, y0, step, nx, ny = grid
    cells = []
    for i, j, z in region:
        nz = next((e[1] for e in fmap.get((i, j), []) if abs(e[0] - z) <= 0.5), None)
        if nz is not None and 0.7 <= nz < 0.99:
            cells.append((i, j, z, nz))
    zof = {(c[0], c[1], round(c[2], 1)): c for c in cells}
    groups = gespots.clusters([(c[0], c[1], c[2]) for c in cells], step, dz=40.0)
    out = []
    for g in groups:
        if len(g) < 6:
            continue
        nzs = sorted(zof.get((c[0], c[1], round(c[2], 1)), (0, 0, 0, 1.0))[3] for c in g)
        nzm = nzs[len(nzs) // 2]
        # Gradient from a least-squares plane z = a x + b y + c over the cells.
        n = len(g)
        sx = sum(c[0] for c in g) / n
        sy = sum(c[1] for c in g) / n
        sz = sum(c[2] for c in g) / n
        sxx = sum((c[0] - sx) ** 2 for c in g)
        syy = sum((c[1] - sy) ** 2 for c in g)
        sxy = sum((c[0] - sx) * (c[1] - sy) for c in g)
        sxz = sum((c[0] - sx) * (c[2] - sz) for c in g)
        syz = sum((c[1] - sy) * (c[2] - sz) for c in g)
        det = sxx * syy - sxy * sxy
        if abs(det) < 1e-9:
            continue
        a = (sxz * syy - syz * sxy) / det / step
        b = (syz * sxx - sxz * sxy) / det / step
        xs = [x0 + c[0] * step for c in g]
        ys = [y0 + c[1] * step for c in g]
        zs = [c[2] for c in g]
        lo = min(g, key=lambda c: c[2])
        hi = max(g, key=lambda c: c[2])
        out.append({'cells': n, 'normalZ': round(nzm, 4), 'degrees': round(math.degrees(math.acos(min(1.0, nzm))), 1),
                    'upYaw': gespots.yaw_of(a, b), 'grade': round(math.hypot(a, b), 4),
                    'box': [[min(xs), min(ys)], [max(xs), max(ys)]], 'z': [round(min(zs), 1), round(max(zs), 1)],
                    'low': [x0 + lo[0] * step, y0 + lo[1] * step, round(lo[2], 2)],
                    'high': [x0 + hi[0] * step, y0 + hi[1] * step, round(hi[2], 2)]})
    out.sort(key=lambda p: -p['cells'])
    return out[:top]


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    ap.add_argument('--map', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--floors', help='floor map cache (spots.py or this tool); built and cached when missing')
    ap.add_argument('--bounds', help='x0,y0,x1,y1 for a new floor map (default: GE bounds grown by 1500)')
    ap.add_argument('--step', type=int, default=32)
    ap.add_argument('--only', default=','.join(PARTS))
    ap.add_argument('--sandbox', default=sb.DEFAULT_SANDBOX)
    ap.add_argument('--re-tools', default=os.environ.get('SVM_RE_TOOLS', sb.DEFAULT_RE_TOOLS))
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding='utf-8')
    want = set(args.only.split(','))
    ges, ge_path = gespots.load_ges(args.map)
    with open(ge_path, encoding='utf-8') as f:
        ge_doc = json.load(f)
    tools = sb.load_tools(args.re_tools)
    result = {'map': args.map, 'geFile': os.path.relpath(ge_path, REPO).replace('\\', '/'),
              'geSha256': sb.sha256(ge_path)}
    with sb.Sandbox(tools, args.sandbox, fps=None) as box:
        box.load_map(args.map)
        live = box.live
        if 'ladder' in want:
            result['ladder'] = survey_ladders(live, ges)
        if 'hoh' in want:
            result['hoh'] = survey_hoh(live, ges)
        if 'fence' in want:
            result['fence'] = survey_fences(live, ges)
        if want & {'barrier', 'walljump'}:
            sides = gespots.survey_lg(live, ges)
            if 'barrier' in want:
                result['barrier'] = barrier_spots(live, sides)
            if 'walljump' in want:
                result['walljump'] = walljump_spots(live, sides)
        if want & {'split', 'steps', 'slopes', 'drops'}:
            cache = args.floors or os.path.splitext(os.path.abspath(args.out))[0] + '.floors.json'
            if os.path.isfile(cache):
                fmap, grid = load_floor_map(cache)
                sb.log('floor map from', cache)
            else:
                if args.bounds:
                    b = [float(v) for v in args.bounds.split(',')]
                else:
                    gb = ge_doc['bounds']
                    b = [gb[0][0] - 1500, gb[0][1] - 1500, gb[1][0] + 1500, gb[1][1] + 1500]
                fmap, grid = build_floor_map(live, ge_doc, [(b[0], b[1]), (b[2], b[3])], args.step, cache)
            region, starts = region_of(live, fmap, grid)
            sb.log('walkable cells:', len(region))
            result['walkableCells'] = len(region)
            if 'split' in want:
                result['split'] = split_corridors(live, fmap, grid, region)
            if 'steps' in want:
                result['steps'], result['stairs'] = step_edges(fmap, grid, region)
            if 'slopes' in want:
                result['slopes'] = slope_patches(fmap, grid, region)
            if 'drops' in want:
                result['drops'] = spots.drop_edges(live, fmap, grid, region, spots.load_ges(args.map), hmin=200.0,
                                                   hmax=700.0)[:80]
        result['editor'] = box.report.get('editor')
        result['mapFile'] = box.report.get('maps')
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(result, f, indent=1)
    for key in ('ladder', 'hoh', 'fence', 'barrier', 'walljump', 'split', 'stairs', 'steps', 'slopes', 'drops'):
        for e in result.get(key, [])[:12]:
            print(key, json.dumps(e))


if __name__ == '__main__':
    main()
