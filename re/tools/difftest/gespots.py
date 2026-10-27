"""Find test spots at a map's traversal GEs (ledges, pipes, zip lines), at crawl gaps and at box walls, with the
real engine's collision through the sandbox editor's live API (point.info floors, trace.box sweeps).

    python -I -B re/tools/difftest/gespots.py --map SteeE --out local/difftest/spots/SteeE-ge.json \\
        [--floors local/difftest/spots/SteeE.floors.json] [--only lg,pipe,zl,crawl,btw]

The editor runs headless in the sandbox; no game is started, so the play lock is not taken. The GE list is the
decoder's output (local/formats/ge/<Map>.json). The result is game-derived data and stays under local/.

What it reports:
- lg: per LG and side, the floor beyond the lip and the drop to it, how close a standing spy gets to the wall
  under the lip (a box sweep toward it), the headroom for a jump there, the room for a standing and a crouched
  spy on the top beyond the lip, the run-up on the top toward the lip, and the corners it shares with other
  LGs. `jumpGrab` picks the spots where a standing jump should catch the ledge (drop 208 to 320 measured by
  RE+, wall under the lip, headroom, flat approach) and says whether there is room to pull up; `walkOff` the
  tops a slow walk can leave over a drop of more than 175 (the back-ledge catch of MyFalling).
- pipe: the approach spot on the climber's side (the side point's direction), the floor at the foot, the lane
  to the pipe and the floor at the top exit.
- zl: the high and low ends, the floor under the line from behind the high end onwards with the line's height
  above it, and the platform under the high end.
- crawl: floor cells reached on foot (the spots.py floor map, flooded from the player starts) where a crouched
  spy fits and a standing one does not, grouped, with crouched lanes through each group.
- btw: walls under LG boxes standing on walkable floor, with the outside corner at each end of the wall, for a
  back-to-wall sidestep that ends in a peek.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import sandbox as sb  # noqa: E402
import spots  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))

# The game's spy cylinder: CollisionHeight 87.5 standing, 50 crouched, radius 35 (R4 measured the 1.0 hover).
SPY_STAND = (35.0, 35.0, 87.5)
SPY_CROUCH = (35.0, 35.0, 50.0)
HOVER = 2.0
JUMP_APEX = 124.0                 # spy JumpZ 610, gravity 1500 (R4 measured 124.0)
GRAB_DROP = (208.0, 320.0)        # RE+ measured: a standing jump catches tops 208 to 320 over the floor
BACK_GRAB_DROP = 175.0            # MyFalling: no floor within 175 below for the back-ledge catch
LG_PROBES = (20.0, 40.0, 64.0, 100.0, 150.0, 200.0, 250.0)
SAME_Z = 2.0


# ---- geometry (no editor) -------------------------------------------------------------------------

def sub(a, b):
    return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]


def unit2d(dx, dy):
    n = math.hypot(dx, dy)
    return (dx / n, dy / n) if n > 1e-9 else (0.0, 0.0)


def left_normal(u):
    """The 2D normal 90 degrees to the left of u (counter-clockwise, seen from above)."""
    return (-u[1], u[0])


def yaw_of(dx, dy):
    """Rotator yaw (0..65535) of a 2D direction."""
    return int(round(math.atan2(dy, dx) * 32768.0 / math.pi)) & 0xFFFF


def offset(p, n, d, z=None):
    return [p[0] + n[0] * d, p[1] + n[1] * d, p[2] if z is None else z]


def lg_frame(points):
    """Midpoint, 2D direction, length and left normal of a ledge's top edge (its first two points)."""
    a, b = points[0], points[1]
    u = unit2d(b[0] - a[0], b[1] - a[1])
    mid = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2]
    return mid, u, math.hypot(b[0] - a[0], b[1] - a[1]), left_normal(u)


def shared_corners(ges, tol=1.0):
    """{(ge index, end 0 or 1): [(other index, other end), ...]} for LG ends on the same point."""
    ends = []
    for g in ges:
        if g['kind'] == 'LG':
            for k in (0, 1):
                ends.append((g['index'], k, g['points'][k]))
    out = {}
    for i, (gi, ki, pi) in enumerate(ends):
        for gj, kj, pj in ends[i + 1:]:
            if gi != gj and all(abs(x - y) <= tol for x, y in zip(pi, pj)):
                out.setdefault((gi, ki), []).append((gj, kj))
                out.setdefault((gj, kj), []).append((gi, ki))
    return out


def corner_kind(g, end, other, other_end):
    """'outside' when the joined ledge turns toward the top side (the box's corner seen from the floor: the
    wall under g ends there), 'inside' when it turns away, 'straight' when it carries on."""
    _, u, _, n = lg_frame(g['points'])
    a = other['points'][other_end]
    b = other['points'][1 - other_end]
    v = unit2d(b[0] - a[0], b[1] - a[1])     # leaving the shared point along the other ledge
    if abs(v[0] * u[0] + v[1] * u[1]) > 0.9:
        return 'straight'
    return v


def line_point(h, l, s):
    """The point s uu (horizontally) from the high end h along the line toward the low end l."""
    d2 = math.hypot(l[0] - h[0], l[1] - h[1])
    t = s / d2 if d2 > 0 else 0.0
    return [h[0] + (l[0] - h[0]) * t, h[1] + (l[1] - h[1]) * t, h[2] + (l[2] - h[2]) * t]


def clusters(cells, step, dz=8.0):
    """Groups of (i, j, z) cells joined through 4-neighbours whose floors are within dz."""
    left = set(cells)
    by_ij = {}
    for c in cells:
        by_ij.setdefault((c[0], c[1]), []).append(c)
    out = []
    while left:
        seed = left.pop()
        group, todo = [seed], [seed]
        while todo:
            i, j, z = todo.pop()
            for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                for c in by_ij.get((i + di, j + dj), []):
                    if c in left and abs(c[2] - z) <= dz:
                        left.discard(c)
                        group.append(c)
                        todo.append(c)
        out.append(sorted(group))
    out.sort(key=len, reverse=True)
    return out


# ---- editor queries -------------------------------------------------------------------------------

def floors(live, points, reach=4000):
    """Floor z (or None) under each point, with its normal z; None where the point is in solid."""
    out = []
    for part in spots.chunks(points, 5000):
        for r in live.point_info(part, floor=True, reach=reach):
            if r.get('solid') or not r.get('floor'):
                out.append(None)
            else:
                f = r['floor']
                out.append((float(f['z']), float(f['normal'][2]) if f.get('normal') else 0.0))
    return out


def sweeps(live, traces, extent):
    out = []
    for part in spots.chunks(traces, 5000):
        out += live.trace_box(part, extent=list(extent))
    return out


def blocked(live, points, extent):
    res = sweeps(live, [{'at': p} for p in points], extent)
    return [bool(r.get('blocked')) for r in res]


def load_ges(map_name):
    path = os.path.join(REPO, 'local', 'formats', 'ge', map_name + '.json')
    if not os.path.isfile(path):
        raise SystemExit('no GE list at %s (run re/formats scctformats.ge first)' % path)
    with open(path, encoding='utf-8') as f:
        d = json.load(f)
    return [{'index': g['index'], 'kind': g['kind'], 'id': g.get('id', 0), 'points': g['points'],
             'active': g.get('active_at_start', True)} for g in d['ges']], path


# ---- ledges ---------------------------------------------------------------------------------------

def survey_lg(live, ges):
    lgs = [g for g in ges if g['kind'] == 'LG']
    by_index = {g['index']: g for g in ges}
    corners = shared_corners(ges)
    probe_pts, probe_keys = [], []
    for g in lgs:
        mid, u, length, n = lg_frame(g['points'])
        for sign in (1, -1):
            nn = (n[0] * sign, n[1] * sign)
            for d in LG_PROBES:
                probe_pts.append(offset(mid, nn, d, mid[2] + 30.0))
                probe_keys.append((g['index'], sign, d))
    fl = dict(zip(probe_keys, floors(live, probe_pts)))
    sides = []
    for g in lgs:
        mid, u, length, n = lg_frame(g['points'])
        z = mid[2]
        for sign in (1, -1):
            nn = (n[0] * sign, n[1] * sign)
            got = [(d, fl[(g['index'], sign, d)]) for d in LG_PROBES]
            lip = [f for d, f in got[:2] if f is not None]
            top = bool(lip) and all(abs(f[0] - z) <= 6.0 for f in lip)
            s = {'ge': g['index'], 'id': g['id'], 'active': g['active'], 'sign': sign, 'mid': [round(v, 1) for v in mid],
                 'length': round(length, 1), 'normal': [round(nn[0], 4), round(nn[1], 4)], 'top': top,
                 'floors': [[d, None if f is None else round(f[0], 2), None if f is None else round(f[1], 4)]
                            for d, f in got]}
            low = [f for d, f in got if d >= 64 and f is not None]
            if not top and low:
                zf = low[0][0]
                s['floorZ'] = round(zf, 2)
                s['drop'] = round(z - zf, 1)
                s['flat'] = all(f is not None and abs(f[0] - zf) <= SAME_Z and f[1] >= 0.99 for d, f in got if d >= 64)
            ends = []
            for k in (0, 1):
                for oi, ok in corners.get((g['index'], k), []):
                    kind = corner_kind(g, k, by_index[oi], ok)
                    if kind != 'straight':
                        turns_to = kind[0] * nn[0] + kind[1] * nn[1]
                        kind = 'outside' if turns_to < 0 else 'inside'
                    ends.append({'end': k, 'ge': oi, 'kind': kind})
            s['corners'] = ends
            sides.append(s)
    # Low sides: sweep a standing spy toward the wall under the lip; headroom over where it stops.
    low = [s for s in sides if 'floorZ' in s]
    traces = []
    for s in low:
        nn = s['normal']
        zc = s['floorZ'] + SPY_STAND[2] + HOVER
        traces.append([offset(s['mid'], nn, 260.0, zc), offset(s['mid'], nn, -40.0, zc)])
    res = sweeps(live, traces, SPY_STAND)
    ups = []
    for s, r in zip(low, res):
        if r.get('startInside'):
            s['wall'] = {'startInside': True}
            continue
        if not r.get('hit'):
            s['wall'] = {'hit': False}
            continue
        p = [float(v) for v in r['point']]
        dist = (p[0] - s['mid'][0]) * s['normal'][0] + (p[1] - s['mid'][1]) * s['normal'][1]
        s['wall'] = {'hit': True, 'contact': [round(v, 1) for v in p], 'fromLine': round(dist, 1),
                     'normal': [round(float(v), 3) for v in r.get('normal', [0, 0, 0])],
                     'actor': r.get('actor'), 'bsp': bool(r.get('bsp'))}
        start = offset(p, s['normal'], 2.0)
        ups.append((s, [start, [start[0], start[1], start[2] + JUMP_APEX + 40.0]]))
    res = sweeps(live, [t for _, t in ups], SPY_STAND)
    for (s, _), r in zip(ups, res):
        s['wall']['headroom'] = round(float(r['dist']), 1) if r.get('hit') else JUMP_APEX + 40.0
    # Top sides: room for a standing and a crouched spy beyond the lip, and the run-up toward the lip.
    tops = [s for s in sides if s['top']]
    pts_st = [offset(s['mid'], s['normal'], 60.0, s['mid'][2] + SPY_STAND[2] + HOVER) for s in tops]
    pts_cr = [offset(s['mid'], s['normal'], 60.0, s['mid'][2] + SPY_CROUCH[2] + HOVER) for s in tops]
    st, cr = blocked(live, pts_st, SPY_STAND), blocked(live, pts_cr, SPY_CROUCH)
    runs = sweeps(live, [[offset(s['mid'], s['normal'], 200.0, s['mid'][2] + SPY_STAND[2] + HOVER),
                          offset(s['mid'], s['normal'], 10.0, s['mid'][2] + SPY_STAND[2] + HOVER)] for s in tops],
                  SPY_STAND)
    for s, b1, b2, r in zip(tops, st, cr, runs):
        flat = 0.0
        for d, z, nz in s['floors']:
            if z is None or abs(z - s['mid'][2]) > 6.0:
                break
            flat = d
        s['room'] = {'stand': not b1, 'crouch': not b2}
        s['runUp'] = {'flat': flat, 'clear': not (r.get('hit') or r.get('startInside'))}
    return sides


def jump_grabs(sides):
    """Low sides where a standing jump should catch the ledge, best first."""
    tops = {(s['ge'], s['sign']): s for s in sides if s['top']}
    out = []
    for s in sides:
        w = s.get('wall') or {}
        if 'drop' not in s or not w.get('hit') or not s.get('flat'):
            continue
        if not GRAB_DROP[0] <= s['drop'] <= GRAB_DROP[1] or not 30.0 <= w['fromLine'] <= 60.0:
            continue
        if w.get('headroom', 0) < JUMP_APEX + 10:
            continue
        top = tops.get((s['ge'], -s['sign']))
        nn = s['normal']
        stand = [round(w['contact'][0] + nn[0] * 100, 1), round(w['contact'][1] + nn[1] * 100, 1), s['floorZ']]
        out.append({'ge': s['ge'], 'length': s['length'], 'drop': s['drop'], 'floorZ': s['floorZ'],
                    'wallFromLine': w['fromLine'], 'headroom': w['headroom'], 'facingYaw': yaw_of(-nn[0], -nn[1]),
                    'start100': stand, 'contact': w['contact'],
                    'pullUp': (top or {}).get('room'), 'corners': s['corners'], 'active': s['active']})
    out.sort(key=lambda c: (not (c['pullUp'] or {}).get('stand'), -c['length'], abs(c['drop'] - 264)))
    return out


def walk_offs(sides):
    """Tops a slow walk can leave toward the lip, over a drop deeper than the back-ledge floor test."""
    lows = {(s['ge'], s['sign']): s for s in sides if 'drop' in s}
    out = []
    for s in sides:
        if not s['top'] or not s.get('runUp') or s['runUp']['flat'] < 100 or not s['runUp']['clear']:
            continue
        low = lows.get((s['ge'], -s['sign']))
        if not low or low['drop'] <= BACK_GRAB_DROP:
            continue
        nn = s['normal']
        start = offset(s['mid'], nn, min(150.0, s['runUp']['flat']), s['mid'][2])
        out.append({'ge': s['ge'], 'length': s['length'], 'topZ': s['mid'][2], 'drop': low['drop'],
                    'start': [round(v, 1) for v in start], 'yaw': yaw_of(-nn[0], -nn[1]), 'runUp': s['runUp'],
                    'room': s.get('room'), 'corners': s['corners'], 'active': s['active']})
    out.sort(key=lambda c: (-c['length'], c['drop']))
    return out


# ---- pipes ----------------------------------------------------------------------------------------

def survey_pipes(live, ges):
    pipes = [g for g in ges if g['kind'] == 'PIPE']
    out = []
    for g in pipes:
        b, t, side = g['points']
        d = unit2d(side[0] - b[0], side[1] - b[1])
        foot = [offset(b, d, k, b[2] + 60.0) for k in (60.0, 120.0, 180.0)]
        exit_ = [offset(t, d, -k, t[2] + 60.0) for k in (40.0, 80.0, 120.0)]
        fl = floors(live, foot + exit_)
        f_foot, f_exit = fl[:3], fl[3:]
        e = {'ge': g['index'], 'bottom': [round(v, 1) for v in b], 'top': [round(v, 1) for v in t],
             'height': round(t[2] - b[2], 1), 'side': [round(d[0], 4), round(d[1], 4)], 'facingYaw': yaw_of(-d[0], -d[1]),
             'footFloors': [None if f is None else round(f[0], 2) for f in f_foot],
             'exitFloors': [None if f is None else round(f[0], 2) for f in f_exit]}
        zf = f_foot[1][0] if f_foot[1] else None
        if zf is not None:
            zc = zf + SPY_STAND[2] + HOVER
            start = offset(b, d, 160.0, zc)
            r = sweeps(live, [[start, offset(b, d, 20.0, zc)]], SPY_STAND)[0]
            e['floorZ'] = round(zf, 2)
            e['start160'] = [round(start[0], 1), round(start[1], 1), round(zf, 2)]
            if r.get('hit'):
                p = [float(v) for v in r['point']]
                e['lane'] = {'hit': True, 'stopsAt': round((p[0] - b[0]) * d[0] + (p[1] - b[1]) * d[1], 1),
                             'actor': r.get('actor'), 'bsp': bool(r.get('bsp'))}
            else:
                e['lane'] = {'hit': False}
        ex = [f[0] for f in f_exit if f is not None]
        e['exitFlush'] = bool(ex) and abs(ex[0] - t[2]) <= 8.0
        out.append(e)
    return out


# ---- zip lines ------------------------------------------------------------------------------------

def survey_zips(live, ges):
    out = []
    for g in ges:
        if g['kind'] != 'ZL':
            continue
        p1, p2 = g['points']
        h, l = (p1, p2) if p1[2] >= p2[2] else (p2, p1)
        d = unit2d(l[0] - h[0], l[1] - h[1])
        length = math.hypot(l[0] - h[0], l[1] - h[1])
        ss = [float(s) for s in range(-320, 801, 16)]
        pts = []
        for s in ss:
            lp = line_point(h, l, max(0.0, s))
            pts.append([h[0] + d[0] * s, h[1] + d[1] * s, lp[2] - 30.0])
        fl = floors(live, pts, reach=1200)
        profile = []
        for s, f in zip(ss, fl):
            lz = line_point(h, l, max(0.0, s))[2]
            profile.append([s, None if f is None else round(f[0], 2), None if f is None else round(lz - f[0], 1)])
        plat = None
        for s, zf, above in profile:
            if s >= 0 and zf is not None and above is not None and above <= 400:
                plat = zf
                break
        e = {'ge': g['index'], 'high': [round(v, 1) for v in h], 'low': [round(v, 1) for v in l],
             'length2d': round(length, 1), 'slope': round((h[2] - l[2]) / length, 4) if length else None,
             'yawDown': yaw_of(d[0], d[1]), 'profile': profile}
        if plat is not None:
            run = [s for s, zf, _ in profile if zf is not None and abs(zf - plat) <= SAME_Z]
            e['platform'] = {'floorZ': plat, 'from': min(run), 'to': max(run),
                             'lineAboveAtHigh': round(h[2] - plat, 1)}
        out.append(e)
    return out


# ---- crawl gaps -----------------------------------------------------------------------------------

def survey_crawl(live, fmap, grid, map_name, top=20):
    x0, y0, step, nx, ny = grid
    starts = spots.player_starts(live)
    region = spots.walkable(fmap, grid, spots.seed_floors(fmap, grid, starts))
    cells = sorted(region)
    st = blocked(live, [[x0 + i * step, y0 + j * step, z + SPY_STAND[2] + HOVER] for i, j, z in cells], SPY_STAND)
    cr = blocked(live, [[x0 + i * step, y0 + j * step, z + SPY_CROUCH[2] + HOVER] for i, j, z in cells], SPY_CROUCH)
    stand_ok = {c for c, b in zip(cells, st) if not b}
    only = [c for c, b1, b2 in zip(cells, st, cr) if b1 and not b2]
    sb.log('crawl: %d walkable cells, %d crouch-only' % (len(cells), len(only)))
    groups = [g for g in clusters(only, step) if len(g) >= 3][:top * 3]
    stand_by_ij = {}
    for c in stand_ok:
        stand_by_ij.setdefault((c[0], c[1]), []).append(c[2])
    out, checks = [], []
    for grp in groups:
        zs = [c[2] for c in grp]
        ci = sum(c[0] for c in grp) / len(grp)
        cj = sum(c[1] for c in grp) / len(grp)
        cz = sorted(zs)[len(zs) // 2]
        e = {'cells': len(grp), 'floorZ': round(cz, 2),
             'box': [[x0 + min(c[0] for c in grp) * step, y0 + min(c[1] for c in grp) * step],
                     [x0 + max(c[0] for c in grp) * step, y0 + max(c[1] for c in grp) * step]],
             'centre': [round(x0 + ci * step, 1), round(y0 + cj * step, 1)], 'lanes': []}
        members = {(c[0], c[1]) for c in grp}
        for k in range(8):
            a = k * math.pi / 4
            ux, uy = math.cos(a), math.sin(a)
            ends = []
            for sign in (1, -1):
                found = None
                inside = 0
                for t in range(1, 40):
                    i = int(round(ci + sign * ux * t))
                    j = int(round(cj + sign * uy * t))
                    if (i, j) in members:
                        inside = t
                        continue
                    zz = [z for z in stand_by_ij.get((i, j), []) if abs(z - cz) <= 34]
                    if zz and t > inside:
                        found = (i, j, zz[0], t)
                        break
                ends.append(found)
            if ends[0] and ends[1]:
                a_, b_ = ends[1], ends[0]
                pa = [x0 + a_[0] * step, y0 + a_[1] * step, a_[2]]
                pb = [x0 + b_[0] * step, y0 + b_[1] * step, b_[2]]
                e['lanes'].append({'from': pa, 'to': pb, 'yaw': yaw_of(pb[0] - pa[0], pb[1] - pa[1]),
                                   'length': round(math.hypot(pb[0] - pa[0], pb[1] - pa[1]), 1)})
                zc = max(pa[2], pb[2]) + SPY_CROUCH[2] + HOVER
                checks.append((e['lanes'][-1], [[pa[0], pa[1], zc], [pb[0], pb[1], zc]]))
        out.append(e)
    res = sweeps(live, [t for _, t in checks], SPY_CROUCH)
    for (lane, _), r in zip(checks, res):
        lane['crouchClear'] = not (r.get('hit') or r.get('startInside'))
        if r.get('hit'):
            lane['crouchHitAt'] = round(float(r['dist']), 1)
    for e in out:
        e['lanes'].sort(key=lambda l: (not l['crouchClear'], l['length']))
    out.sort(key=lambda e: (-any(l['crouchClear'] for l in e['lanes']), -e['cells']))
    return out[:top]


# ---- back-to-wall -----------------------------------------------------------------------------------

def btw_walls(sides):
    """Walls under an LG whose low side is a flat floor the wall rises well above a standing spy from, with an
    outside corner at an end (the wall ends there, so a sidestep along it reaches a peek)."""
    out = []
    for s in sides:
        w = s.get('wall') or {}
        if 'drop' not in s or not w.get('hit') or not s.get('flat') or s['drop'] < 190:
            continue
        if not 30.0 <= w['fromLine'] <= 45.0:
            continue
        outs = [c for c in s['corners'] if c['kind'] == 'outside']
        if not outs:
            continue
        out.append({'ge': s['ge'], 'length': s['length'], 'wallHeight': s['drop'], 'floorZ': s['floorZ'],
                    'contact': w['contact'], 'backToWallYaw': yaw_of(s['normal'][0], s['normal'][1]),
                    'faceWallYaw': yaw_of(-s['normal'][0], -s['normal'][1]), 'outsideCornerEnds': outs})
    out.sort(key=lambda c: -c['length'])
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    ap.add_argument('--map', default='SteeE')
    ap.add_argument('--out', required=True)
    ap.add_argument('--floors', help='floor map cache written by spots.py (needed for crawl)')
    ap.add_argument('--only', default='lg,pipe,zl,crawl,btw')
    ap.add_argument('--sandbox', default=sb.DEFAULT_SANDBOX)
    ap.add_argument('--re-tools', default=os.environ.get('SVM_RE_TOOLS', sb.DEFAULT_RE_TOOLS))
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding='utf-8')
    want = set(args.only.split(','))
    ges, ge_path = load_ges(args.map)
    tools = sb.load_tools(args.re_tools)
    result = {'map': args.map, 'geFile': os.path.relpath(ge_path, REPO).replace('\\', '/'),
              'geSha256': sb.sha256(ge_path)}
    with sb.Sandbox(tools, args.sandbox, fps=None) as box:
        box.load_map(args.map)
        live = box.live
        sides = None
        if want & {'lg', 'btw'}:
            sides = survey_lg(live, ges)
        if 'lg' in want:
            result['lg'] = sides
            result['jumpGrab'] = jump_grabs(sides)
            result['walkOff'] = walk_offs(sides)
        if 'btw' in want:
            result['btw'] = btw_walls(sides)
        if 'pipe' in want:
            result['pipe'] = survey_pipes(live, ges)
        if 'zl' in want:
            result['zl'] = survey_zips(live, ges)
        if 'crawl' in want:
            if not args.floors:
                raise SystemExit('crawl needs --floors (the floor map cache spots.py writes)')
            with open(args.floors, encoding='utf-8') as f:
                c = json.load(f)
            fmap = {tuple(int(v) for v in k.split(',')): [tuple(e) for e in v] for k, v in c['floors'].items()}
            result['crawl'] = survey_crawl(live, fmap, tuple(c['grid']), args.map)
        result['editor'] = box.report.get('editor')
        result['mapFile'] = box.report.get('maps')
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(result, f, indent=1)
    for key in ('jumpGrab', 'walkOff', 'btw'):
        for e in result.get(key, [])[:8]:
            print(key, json.dumps(e))
    for e in result.get('pipe', []):
        print('pipe', json.dumps(e))
    for e in result.get('zl', []):
        print('zl', json.dumps({k: v for k, v in e.items() if k != 'profile'}))
    for e in result.get('crawl', [])[:10]:
        print('crawl', json.dumps(dict(e, lanes=e['lanes'][:2])))


if __name__ == '__main__':
    main()
