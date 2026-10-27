"""Find flat, open test spots in a map with the real engine's collision, through the sandbox editor's
live API (point.info floors and trace.box sweeps with the player's cylinder sizes).

    python -I -B re/tools/difftest/spots.py --map SteeE --out local/difftest/spots/SteeE.json

The editor runs headless in the sandbox; no game is started, so the play lock is not taken. The
result is game-derived data (map geometry) and stays under local/.

What it does:
1. A floor map: point.info on a grid (default 32 uu) at a ladder of heights; every distinct floor
   under a column with its normal.
2. Lanes: from candidate floors (a coarser grid), in 16 directions, a box sweep at standing height
   with a widened spy box gives the clear length; the floor map then cuts the lane where the floor
   is no longer the same flat floor. GEs (from local/formats/ge/<Map>.json, when present) near a
   lane are reported with their distance.
3. Ledges: lanes whose floor ends in a drop, with the height of the drop and the clearance below.
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

import sandbox as sb  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))

SPY_STAND = (35.0, 35.0, 89.0)
LANE_MARGIN = 25.0          # side clearance added to the spy radius for a lane sweep
FLAT_NZ = 0.9995            # floor normal z for "flat"
SAME_FLOOR = 1.0            # uu: a lane stays on one floor height
FLIGHT_SPEEDS = (430, 230, 175)   # uu/s off an edge: spy fast, crouched, slow


def chunks(items, size):
    for i in range(0, len(items), size):
        yield items[i:i + size]


def floor_map(live, bounds, step, levels):
    """{(i, j): [(z, nz), ...]} distinct floors under each grid column."""
    (x0, y0), (x1, y1) = bounds
    nx, ny = int((x1 - x0) // step) + 1, int((y1 - y0) // step) + 1
    points, keys = [], []
    for i in range(nx):
        for j in range(ny):
            for z in levels:
                points.append([x0 + i * step, y0 + j * step, z])
                keys.append((i, j))
    out = {}
    t = time.time()
    for start in range(0, len(points), 5000):
        res = live.point_info(points[start:start + 5000], floor=True, reach=4000)
        for k, r in zip(keys[start:start + 5000], res):
            if r.get('solid') or not r.get('floor'):
                continue
            f = r['floor']
            z = round(float(f['z']), 2)
            nz = float(f['normal'][2]) if f.get('normal') else 0.0
            cell = out.setdefault(k, [])
            if all(abs(z - e[0]) > 0.5 for e in cell):
                cell.append((z, nz))
    sb.log('floor map: %d columns, %d points in %.1fs' % (nx * ny, len(points), time.time() - t))
    return out, (x0, y0, step, nx, ny)


def walkable(fmap, grid, seeds, step_up=32.0, min_nz=0.7):
    """Floors reachable on foot from the seed points: a flood fill over the floor map between
    4-neighbour columns whose floors differ by at most a step. Thin walls between two floors of the
    same height are not seen (the map has no walls), which only matters for the region's edges."""
    x0, y0, step, nx, ny = grid
    seen = set()
    todo = []
    for sx, sy, sz in seeds:
        i, j = int(round((sx - x0) / step)), int(round((sy - y0) / step))
        for z, nz in fmap.get((i, j), []):
            if abs(z - sz) <= 40:
                todo.append((i, j, z))
    while todo:
        i, j, z = todo.pop()
        if (i, j, z) in seen:
            continue
        seen.add((i, j, z))
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            for z2, nz2 in fmap.get((i + di, j + dj), []):
                if nz2 >= min_nz and abs(z2 - z) <= step_up and (i + di, j + dj, z2) not in seen:
                    todo.append((i + di, j + dj, z2))
    return seen


def floor_at(fmap, grid, x, y, z, tol=SAME_FLOOR):
    """(z, nz) of the floor at (x, y) within tol of z, else the nearest floor below z, else None."""
    x0, y0, step, nx, ny = grid
    i, j = int(round((x - x0) / step)), int(round((y - y0) / step))
    cell = fmap.get((i, j)) or []
    same = [e for e in cell if abs(e[0] - z) <= tol]
    if same:
        return same[0], True
    below = sorted((e for e in cell if e[0] < z - tol), key=lambda e: -e[0])
    return (below[0] if below else None), False


def load_ges(map_name):
    path = os.path.join(REPO, 'local', 'formats', 'ge', map_name + '.json')
    if not os.path.isfile(path):
        return []
    with open(path, encoding='utf-8') as f:
        d = json.load(f)
    return [{'index': g['index'], 'kind': g['kind'], 'points': g['points']} for g in d.get('ges', [])]


def seg_dist2d(p, a, b):
    ax, ay = a[0], a[1]
    bx, by = b[0], b[1]
    dx, dy = bx - ax, by - ay
    L = dx * dx + dy * dy
    t = 0.0 if L == 0 else max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - ay) * dy) / L))
    return math.hypot(p[0] - (ax + t * dx), p[1] - (ay + t * dy))


def nearest_ge(ges, start, end, z, zrange=400.0):
    best = None
    for g in ges:
        pts = g['points']
        gz = sum(p[2] for p in pts) / len(pts)
        if abs(gz - z) > zrange:
            continue
        samples = [start, end] + [[start[0] + (end[0] - start[0]) * k / 8, start[1] + (end[1] - start[1]) * k / 8]
                                  for k in range(1, 8)]
        d = min(seg_dist2d(s, pts[0], pts[-1]) for s in samples)
        if best is None or d < best[0]:
            best = (round(d, 1), g['kind'], g['index'], round(gz - z, 1))
    return best


def player_starts(live):
    found = live.find(class_='PlayerStart', fields=['name', 'location'])
    return [[float(v) for v in a['location']] for a in found if a.get('location')]


def seed_floors(fmap, grid, starts):
    """The floor under each player start: the highest floor 0 to 120 below it."""
    x0, y0, step, nx, ny = grid
    seeds = []
    for sx, sy, sz in starts:
        i, j = int(round((sx - x0) / step)), int(round((sy - y0) / step))
        below = [z for z, nz in fmap.get((i, j), []) if 0 <= sz - z <= 120]
        if below:
            seeds.append((sx, sy, max(below)))
    return seeds


def survey(live, map_name, bounds, step=32, lane_step=96, max_lane=1800, top=40, cache=None):
    levels = list(range(520, -1500, -110))
    if cache and os.path.isfile(cache):
        with open(cache, encoding='utf-8') as f:
            c = json.load(f)
        fmap = {tuple(int(v) for v in k.split(',')): [tuple(e) for e in v] for k, v in c['floors'].items()}
        grid = tuple(c['grid'])
        sb.log('floor map from', cache)
    else:
        fmap, grid = floor_map(live, bounds, step, levels)
        if cache:
            with open(cache, 'w', encoding='utf-8') as f:
                json.dump({'grid': list(grid), 'floors': {'%d,%d' % k: v for k, v in fmap.items()}}, f)
    x0, y0, _, nx, ny = grid
    starts = player_starts(live)
    seeds = seed_floors(fmap, grid, starts)
    region = walkable(fmap, grid, seeds)
    sb.log('walkable: %d floor cells from %d player starts' % (len(region), len(seeds)))

    def on_floor(px, py, z):
        i, j = int(round((px - x0) / step)), int(round((py - y0) / step))
        for z2, nz2 in fmap.get((i, j), []):
            if abs(z2 - z) <= SAME_FLOOR and (i, j, z2) in region:
                return nz2
        return None

    ratio = max(1, int(lane_step // step))
    cands = []
    for (i, j, z) in region:
        if i % ratio or j % ratio:
            continue
        nz = on_floor(x0 + i * step, y0 + j * step, z)
        if nz is not None and nz >= FLAT_NZ:
            cands.append([x0 + i * step, y0 + j * step, z])
    fits = []
    for part in chunks(cands, 5000):
        res = live.trace_box([{'at': [c[0], c[1], c[2] + SPY_STAND[2] + 2]} for c in part], size='spy')
        fits += [c for c, r in zip(part, res) if not r.get('blocked')]
    sb.log('candidates: %d flat walkable, %d fit a standing spy' % (len(cands), len(fits)))
    ext = [SPY_STAND[0] + LANE_MARGIN, SPY_STAND[1] + LANE_MARGIN, SPY_STAND[2]]
    sweeps, meta = [], []
    for c in fits:
        zc = c[2] + SPY_STAND[2] + 2
        for d in range(16):
            yaw = d * 4096
            a = yaw * math.pi / 32768.0
            ux, uy = math.cos(a), math.sin(a)
            sweeps.append([[c[0], c[1], zc], [c[0] + ux * max_lane, c[1] + uy * max_lane, zc]])
            meta.append((c, yaw, ux, uy))
    lanes, ledges = [], []
    t = time.time()
    for start in range(0, len(sweeps), 5000):
        res = live.trace_box(sweeps[start:start + 5000], extent=ext)
        for (c, yaw, ux, uy), r in zip(meta[start:start + 5000], res):
            if r.get('startInside'):
                continue
            clear = float(r['dist']) if r.get('hit') else max_lane
            # The same flat floor along the lane and 64 to either side of it.
            flat, drop, s = 0.0, None, 16.0
            while s <= clear:
                px, py = c[0] + ux * s, c[1] + uy * s
                ok = True
                for side in (-64.0, 0.0, 64.0):
                    nz = on_floor(px - uy * side, py + ux * side, c[2])
                    if nz is None or nz < FLAT_NZ:
                        ok = False
                        if side == 0.0:
                            f, same = floor_at(fmap, grid, px, py, c[2])
                            if f is not None and not same:
                                drop = {'at': round(s, 1), 'below': round(c[2] - f[0], 1),
                                        'belowNz': round(f[1], 4)}
                        break
                if not ok:
                    break
                flat = s
                s += 16.0
            lane = {'start': [round(c[0], 1), round(c[1], 1), round(c[2], 2)], 'yaw': yaw,
                    'clear': round(clear, 1), 'flat': round(flat, 1)}
            if drop and drop['below'] > 300 and clear > flat + 200:
                lane['drop'] = drop
                ledges.append(lane)
            lanes.append(lane)
    sb.log('lanes: %d swept in %.1fs' % (len(lanes), time.time() - t))
    ges = load_ges(map_name)
    for l in lanes + ledges:
        length = l['drop']['at'] if 'drop' in l else min(l['clear'], l['flat'])
        a = l['yaw'] * math.pi / 32768.0
        end = [l['start'][0] + math.cos(a) * length, l['start'][1] + math.sin(a) * length]
        l['length'] = round(min(l['clear'], l['flat']), 1)
        l['nearestGE'] = nearest_ge(ges, l['start'], end, l['start'][2], zrange=1600 if 'drop' in l else 400)
    lanes = [l for l in lanes if l['length'] >= 400]
    lanes.sort(key=lambda l: -l['length'])
    ledges.sort(key=lambda l: -l['drop']['below'])
    drops = drop_edges(live, fmap, grid, region, ges)
    return {'map': map_name, 'grid': {'step': step, 'laneStep': lane_step, 'levels': levels,
                                      'bounds': bounds, 'columns': nx * ny},
            'starts': starts, 'seeds': seeds, 'walkableCells': len(region),
            'floors': sum(len(v) for v in fmap.values()), 'candidates': len(fits),
            'lanes': lanes[:top * 25], 'ledges': ledges[:top * 25], 'drops': drops[:top * 25],
            '_region': sorted(region), '_grid': list(grid)}


def drop_edges(live, fmap, grid, region, ges, hmin=620.0, hmax=1300.0, runup=192.0, landing=320.0, step=96):
    """Edges to walk off for a fall of known height: a flat floor (any floor, reached on foot or not)
    that runs `runup` uu in a direction and then ends over a flat, walkable floor hmin to hmax below,
    which stays flat for `landing` uu beyond the edge. Checked with sweeps: the run-up at standing
    height, the air beyond the edge, and the fall column down to the lower floor."""
    x0, y0, gstep, nx, ny = grid
    walk = {(i, j): z for i, j, z in region}
    by_cell = {}
    for i, j, z in region:
        by_cell.setdefault((i, j), []).append(z)

    def floor_same(px, py, z):
        i, j = int(round((px - x0) / gstep)), int(round((py - y0) / gstep))
        for z2, nz2 in fmap.get((i, j), []):
            if abs(z2 - z) <= SAME_FLOOR:
                return nz2
        return None

    def lower_walkable(px, py, z):
        i, j = int(round((px - x0) / gstep)), int(round((py - y0) / gstep))
        below = [z2 for z2 in by_cell.get((i, j), []) if z2 < z - 1]
        return max(below) if below else None

    found = []
    ratio = max(1, int(step // gstep))
    for (i, j), floors in fmap.items():
        if i % ratio or j % ratio:
            continue
        for z, nz in floors:
            if nz < FLAT_NZ:
                continue
            x, y = x0 + i * gstep, y0 + j * gstep
            for d in range(16):
                yaw = d * 4096
                a = yaw * math.pi / 32768.0
                ux, uy = math.cos(a), math.sin(a)
                s = 16.0
                while s <= 2000 and all((floor_same(x + ux * s - uy * o, y + uy * s + ux * o, z) or 0) >= FLAT_NZ
                                        for o in (-48.0, 0.0, 48.0)):
                    s += 16.0
                if s < runup:
                    continue
                edge = s
                low = lower_walkable(x + ux * (edge + 48), y + uy * (edge + 48), z)
                if low is None or not hmin <= z - low <= hmax:
                    continue
                ok = True
                for k in range(3, int(landing // 16)):
                    zz = lower_walkable(x + ux * (edge + 16 * k), y + uy * (edge + 16 * k), z)
                    if zz is None or abs(zz - low) > SAME_FLOOR:
                        ok = False
                        break
                if ok:
                    found.append({'start': [round(x, 1), round(y, 1), round(z, 2)], 'yaw': yaw, 'edge': round(edge, 1),
                                  'drop': round(z - low, 1), 'lowerZ': round(low, 2)})
    sb.log('drop edges before sweeps:', len(found))
    if not found:
        return []
    checks = []
    for e in found:
        a = e['yaw'] * math.pi / 32768.0
        ux, uy = math.cos(a), math.sin(a)
        x, y, z = e['start']
        zc = z + SPY_STAND[2] + 2
        far = e['edge'] + 420
        checks.append([[x, y, zc], [x + ux * far, y + uy * far, zc]])
        bx, by = x + ux * (e['edge'] + 200), y + uy * (e['edge'] + 200)
        checks.append([[bx, by, zc], [bx, by, e['lowerZ'] + SPY_STAND[2] + 2]])
        # The flight of a run off the edge at full speed: 430 uu/s out, gravity 1500, in 0.1 s steps
        # until the pawn's middle reaches the lower floor's standing height.
        e['_flight'] = []
        for speed in FLIGHT_SPEEDS:
            flight = []
            t, prev = 0.0, None
            while True:
                hz = zc - 750.0 * t * t
                hs = e['edge'] + 40 + speed * t
                p = [x + ux * hs, y + uy * hs, max(hz, e['lowerZ'] + SPY_STAND[2] + 2)]
                if prev:
                    flight.append([prev, p])
                if hz <= e['lowerZ'] + SPY_STAND[2] + 2:
                    break
                prev, t = p, t + 0.1
            e['_flight'].append(len(flight))
            checks += flight
    res = []
    for part in chunks(checks, 5000):
        res += live.trace_box(part, extent=[SPY_STAND[0] + 8, SPY_STAND[1] + 8, SPY_STAND[2]])
    out = []
    k0 = 0
    for e in found:
        across, down = res[k0], res[k0 + 1]
        k = k0 + 2
        e['flightClear'] = {}
        for speed, n in zip(FLIGHT_SPEEDS, e['_flight']):
            e['flightClear'][str(speed)] = not any(r.get('hit') or r.get('startInside') for r in res[k:k + n])
            k += n
        k0 = k
        del e['_flight']
        if across.get('hit') or across.get('startInside'):
            continue
        if down.get('startInside') or (down.get('hit') and float(down['dist']) < e['drop'] - 8):
            continue

        a = e['yaw'] * math.pi / 32768.0
        end = [e['start'][0] + math.cos(a) * e['edge'], e['start'][1] + math.sin(a) * e['edge']]
        e['nearestGE'] = nearest_ge(ges, e['start'], end, e['start'][2], zrange=1600)
        out.append(e)
    out.sort(key=lambda e: (-sum(e['flightClear'].values()), -(e['nearestGE'][0] if e['nearestGE'] else 9999),
                            e['drop']))
    sb.log('drop edges clear of geometry:', len(out))
    return out


def picture(result, path):
    """A top view of the walkable floor cells (shaded by height) with the best lanes, for review."""
    from PIL import Image, ImageDraw
    x0, y0, step, nx, ny = result['_grid']
    zs = [z for _, _, z in result['_region']] or [0]
    lo, hi = min(zs), max(zs)
    scale = 2
    img = Image.new('RGB', (nx * scale, ny * scale), (20, 20, 20))
    px = img.load()
    for i, j, z in sorted(result['_region'], key=lambda c: c[2]):
        v = int(60 + 190 * (z - lo) / max(1.0, hi - lo))
        for a in range(scale):
            for b in range(scale):
                px[i * scale + a, (ny - 1 - j) * scale + b] = (v, v, v)
    d = ImageDraw.Draw(img)

    def to_px(x, y):
        return ((x - x0) / step * scale, (ny - 1 - (y - y0) / step) * scale)
    for l in result['lanes'][:60]:
        a = l['yaw'] * math.pi / 32768.0
        sx, sy = l['start'][0], l['start'][1]
        d.line([to_px(sx, sy), to_px(sx + math.cos(a) * l['length'], sy + math.sin(a) * l['length'])],
               fill=(60, 200, 60), width=1)
    for l in result['ledges'][:30]:
        a = l['yaw'] * math.pi / 32768.0
        sx, sy = l['start'][0], l['start'][1]
        d.line([to_px(sx, sy), to_px(sx + math.cos(a) * l['drop']['at'], sy + math.sin(a) * l['drop']['at'])],
               fill=(220, 80, 60), width=1)
    for sx, sy, sz in result['starts']:
        x, y = to_px(sx, sy)
        d.ellipse([x - 3, y - 3, x + 3, y + 3], outline=(80, 160, 255))
    img.save(path)


def check_lane(live, start, yaw, length, size='spy'):
    """Detail of one lane: floor every 16 uu (z, normal), the sweep at standing height and at the
    crouched height, for writing a scenario's spot block."""
    a = yaw * math.pi / 32768.0
    ux, uy = math.cos(a), math.sin(a)
    pts = [[start[0] + ux * s, start[1] + uy * s, start[2] + 60] for s in range(0, int(length) + 1, 16)]
    floors = live.point_info(pts, floor=True, reach=4000)
    zs = [f['floor']['z'] if f.get('floor') else None for f in floors]
    nzs = [f['floor']['normal'][2] if f.get('floor') else None for f in floors]
    sweep = live.trace_box([[[start[0], start[1], start[2] + 91], [start[0] + ux * length, start[1] + uy * length,
                                                                   start[2] + 91]]], size=size)[0]
    mid = [start[0] + ux * length / 2, start[1] + uy * length / 2, start[2] + 90]
    near = live.find(near=[mid, length / 2 + 400], fields=['name', 'class', 'location'])
    skip = ('StaticMeshActor', 'Light', 'Emitter', 'AmbientSound', 'Brush')
    near = [{'name': a.get('name'), 'class': a.get('class'), 'location': a.get('location')} for a in near
            if not any(str(a.get('class', '')).startswith(k) for k in skip)]
    return {'floorZ': {'min': min(z for z in zs if z is not None), 'max': max(z for z in zs if z is not None)},
            'normalZmin': min(n for n in nzs if n is not None), 'missing': sum(1 for z in zs if z is None),
            'sweep': sweep, 'zones': sorted({str(f.get('zoneName')) for f in floors}), 'near': near}


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    ap.add_argument('--map', default='SteeE')
    ap.add_argument('--out', required=True)
    ap.add_argument('--sandbox', default=sb.DEFAULT_SANDBOX)
    ap.add_argument('--re-tools', default=os.environ.get('SVM_RE_TOOLS', sb.DEFAULT_RE_TOOLS))
    ap.add_argument('--bounds', default='-5100,-2800,-200,3200', help='x0,y0,x1,y1')
    ap.add_argument('--step', type=int, default=32)
    ap.add_argument('--lane-step', type=int, default=96)
    ap.add_argument('--check', help='JSON list of [x, y, floorZ, yaw, length] lanes to detail instead of a survey')
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding='utf-8')
    b = [float(v) for v in args.bounds.split(',')]
    tools = sb.load_tools(args.re_tools)
    with sb.Sandbox(tools, args.sandbox, fps=None) as box:
        box.load_map(args.map)
        if args.check:
            out = []
            for x, y, z, yaw, length in json.loads(args.check):
                out.append({'lane': [x, y, z, yaw, length], **check_lane(box.live, [x, y, z], yaw, length)})
            result = {'map': args.map, 'checks': out}
        else:
            cache = os.path.splitext(os.path.abspath(args.out))[0] + '.floors.json'
            result = survey(box.live, args.map, [(b[0], b[1]), (b[2], b[3])], args.step, args.lane_step,
                            cache=cache)
            try:
                picture(result, os.path.splitext(os.path.abspath(args.out))[0] + '.png')
            except ImportError:
                pass
            result.pop('_region', None)
            result.pop('_grid', None)
        result['editor'] = box.report.get('editor')
        result['mapFile'] = box.report.get('maps')
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(result, f, indent=1)
    print(json.dumps({k: v for k, v in result.items()
                      if k not in ('lanes', 'ledges', 'drops', 'checks', 'starts', 'seeds')},
                     indent=1))
    if 'lanes' in result:
        for l in result['lanes'][:15]:
            print('lane', json.dumps(l))
        for l in result['ledges'][:5]:
            print('ledge', json.dumps(l))
        for l in result['drops'][:25]:
            print('drop', json.dumps(l))
    else:
        for c in result['checks']:
            print(json.dumps(c))


if __name__ == '__main__':
    main()
