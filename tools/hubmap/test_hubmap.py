"""Checks of the Hub layout, its world BSP and the tracked folder and manifest (no game needed).

    python -B -m pytest tools/hubmap -q
"""

from __future__ import annotations

import importlib.util
import math
import random
import json
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def _load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


G = _load("hub_generate", HERE / "generate.py")
L = G.L
B = G.B


def _truth(carves, solids, p) -> bool:
    def inside(c):
        return all(pl.dist(p) < 0 for pl in c.planes)

    return any(inside(c) for c in carves) and not any(inside(s) for s in solids)


def test_world_bsp_classifies_points_as_the_volumes_do():
    carves, solids = G.world_volumes()
    root = B.build(G.world_polys(), [], 1, balance=0.25)
    rng = random.Random(11)
    bad = []
    for _ in range(4000):
        p = tuple(rng.uniform(L.HALL_LO[i]-100, L.HALL_HI[i]+100) for i in range(3))
        if B.classify_point(root, p) != _truth(carves, solids, p):
            bad.append(p)
    assert not bad, bad[:5]


def test_spatial_csg_matches_full_subtraction_for_touching_and_overlapping_brushes():
    carves, solids = G.world_volumes()
    # Plaza, benches, and decks include touching faces and overlapping details. Include distant course solids too.
    sample=solids[:90]+solids[500:520]+solids[-20:]
    ordinary=B.boundary(carves,sample,lambda p,i:[p])
    spatial=B.boundary(carves,sample,lambda p,i:[p],spatial=True)
    assert [(p.verts,p.material,p.plane) for p in ordinary]==[(p.verts,p.material,p.plane) for p in spatial]


def test_starts_stand_on_open_floor_apart_from_each_other():
    carves, solids = G.world_volumes()
    starts = [a for a in L.ACTORS if a.cls == "Engine.PlayerStart"]
    assert len(starts) == 64
    assert sum(1 for a in starts if a.props["TeamNumber"] == 0) == 32
    for a in starts:
        x, y, z = a.location
        assert z == L.START_Z
        # Open space for a standing pawn (radius 35, 86 half height round the start), floor under it.
        for dx, dy in ((0, 0), (40, 0), (-40, 0), (0, 40), (0, -40)):
            for dz in (-80, 0, 80):
                assert _truth(carves, solids, (x + dx, y + dy, z + dz)), (a.id, dx, dy, dz)
        assert not _truth(carves, solids, (x, y, -1.0))
    for i, a in enumerate(starts):
        for b in starts[i + 1:]:
            assert math.dist(a.location[:2], b.location[:2]) >= 100.0, (a.id, b.id)


def test_play_spaces_are_clear_floor():
    """The pad courts (where players queue) and the aim range's lane are open floor; dressing stays round them."""
    carves, solids = G.world_volumes()
    spaces = []
    for area in L.AREAS:
        if area.kind == "matchPads":
            spaces += [(p["id"], p["lo"], p["hi"]) for p in area.data["pads"]]
        if area.kind == "aimRange":
            spaces.append((area.id, (area.data["backstop"] + 1, area.data["lane"][0]),
                           (area.data["firingLine"] - 65, area.data["lane"][1])))
    assert len(spaces) == 5
    for ident, (x0, y0), (x1, y1) in spaces:
        rng = random.Random(ident)
        for _ in range(400):
            p = (rng.uniform(x0, x1), rng.uniform(y0, y1), 40.0)
            assert _truth(carves, solids, p), (ident, p)


def test_every_pad_has_every_theme_kit_and_a_sign_anchor():
    pads = next(a for a in L.AREAS if a.kind == "matchPads").data["pads"]
    for pad in pads:
        assert pad["theme"] in L.THEMES
        assert len(pad["signAnchor"]) == 3
        for theme in L.THEMES:
            assert f"HubTheme_{pad['id']}_{theme}" in L.GROUPS


def test_zip_line_matches_testrange():
    hx, hy, hz = L.ZIP_HIGH
    lx, ly, lz = L.ZIP_LOW
    assert (hx - lx, hy - ly) == (0.0, 1760.0)
    assert hz - lz == 320.0
    assert hz - 384.0 == 304.0
    assert lz - 160.0 == 208.0


def test_no_stock_names_in_the_folder():
    folder = G.DEFAULT_OUT
    for p in folder.rglob("*"):
        if p.suffix in (".json",):
            text = p.read_text(encoding="utf-8")
            assert "_TXT" not in text and "_txt" not in text, p


def test_tracked_folder_and_manifest_are_current():
    assert G.check(G.DEFAULT_OUT) == [], "run python -B tools/hubmap/generate.py"


def test_expansion_has_separate_courses_clear_gates_and_section_landings():
    assert tuple(L.HALL_HI[i]-L.HALL_LO[i] for i in range(3)) == (49152,49152,16384)
    courses={a.data["courseId"]:a for a in L.AREAS if a.kind=="movementCourse"}
    assert set(courses)=={"easy","medium","hard"}
    assert courses["easy"].data["revision"]==4 # added perimeter grabs change the scored traversal surface
    for ident,count,limit in (("medium",6,2700),("hard",8,5400)):
        a=courses[ident];gates=a.data["checkpoints"];sections=a.data["sections"]
        assert len(sections)==count and a.data["maxSeconds"]==limit
        assert sections[0]["firstGate"]==0
        assert all(x["firstGate"]<y["firstGate"] for x,y in zip(sections,sections[1:]))
        assert all(s["failures"] for s in sections)
        for p in [g["at"] for g in gates]+[s["recovery"] for s in sections]:
            # Standing pawn, floor below it and a clear camera directly overhead.
            assert not L._box_hits((p[0],p[1],p[2]+94),(35,35,86)),(ident,p,L._box_hits((p[0],p[1],p[2]+94),(35,35,86)))
            assert not L.MESH_CONTACTS.hits((p[0],p[1],p[2]+94),(35,35,86)),(ident,p,'model/detail collision')
            assert L._box_hits((p[0],p[1],p[2]-1),(1,1,1)),(ident,p,"no floor")
        assert all(g["height"]<=180 for g in gates)


def test_teleport_arrivals_are_clear_and_outside_courses_and_queues():
    assert len({t["id"] for t in L.TELEPORTS})==8
    courses=[a for a in L.AREAS if a.kind=="movementCourse"]
    pads=next(a for a in L.AREAS if a.kind=="matchPads").data["pads"]
    for t in L.TELEPORTS:
        assert len(t["arrivals"])>=8
        for p in t["arrivals"]:
            assert not L._box_hits((p[0],p[1],p[2]+94),(40,40,86)),(t["id"],p)
            assert not L.MESH_CONTACTS.hits((p[0],p[1],p[2]+94),(40,40,86)),(t['id'],p,'scenery blocks arrival')
            for c in courses: assert math.dist(p[:2],c.data["start"][:2])>320
            for pad in pads: assert not (pad["lo"][0]<=p[0]<=pad["hi"][0] and pad["lo"][1]<=p[1]<=pad["hi"][1])


def test_movers_have_matching_actors_and_clear_swept_hulls():
    assert {m["id"] for m in L.MOVERS}=={"mediumLift","hardLift","hardShuttle"}
    timings=json.loads((G.DEFAULT_OUT/'sim/movers.json').read_text())
    assert timings['format']=='svm.movers/1'
    assert {m['actor']:m['keys'][1]['time'] for m in timings['movers']}=={'mediumLift':3.5,'hardLift':3.5,'hardShuttle':8.0}
    for m in L.MOVERS:
        actor=next(a for a in L.ACTORS if a.id==m["id"])
        assert actor.cls=="Engine.Mover" and actor.props["bAlwaysRelevant"]
        assert actor.props["InitialState"]=="TriggerPound"
        assert actor.props['MoveTime']==next(t['keys'][1]['time'] for t in timings['movers'] if t['actor']==m['id'])
        attached=[g for g in G.build_traversal()['ges'] if g.get('attachedTo')==m['id']]
        assert len(attached)==4 and all(g['kind']=='LG' for g in attached)
        for i in range(33):
            p=tuple(m["at"][axis]+(m["end"][axis]-m["at"][axis])*i/32 for axis in range(3))
            assert not L._box_hits(p,m["half"]),(m["id"],i,L._box_hits(p,m["half"]))


def test_static_render_chunks_are_spatially_bounded_and_movers_are_local():
    data=(G.DEFAULT_OUT/"render/world.glb").read_bytes()
    length=struct.unpack_from("<I",data,12)[0]
    gltf=json.loads(data[20:20+length])
    for mesh in gltf["meshes"]:
        for primitive in mesh["primitives"]:
            accessor=gltf["accessors"][primitive["attributes"]["POSITION"]]
            assert max(b-a for a,b in zip(accessor["min"],accessor["max"]))<=5120,mesh["name"]
    instances=gltf["extras"]["svmap"]["instances"]
    moving=[i for i in instances if i["kind"]=="mover"]
    assert len(moving)==3 and all(i["gameplay"] for i in moving)


def test_credited_scenery_is_embedded_and_matches_the_source_lock():
    import hashlib
    validator=_load('hub_glb_validator',HERE.parent/'assets'/'validate_glb.py')
    assets=HERE/'assets'
    sources={r['asset']:r for r in json.loads((assets/'sources.json').read_text())}
    assert 'GamesNotDeveloped' in (assets/'CREDITS.txt').read_text(encoding='utf8')
    for name in {i['asset'] for i in L.SCENERY}|{L.FOLIAGE['fernAsset']}:
        path=assets/name;record=sources[path.stem]
        assert record['license'] in ('CC-BY-4.0','CC0-1.0')
        assert hashlib.sha256(path.read_bytes()).hexdigest()==record['sha256']
        document,_=validator.read_glb(path)
        assert not any('uri' in b for b in document['buffers'])
        assert not any('uri' in i for i in document.get('images',[]))
        result=validator.validate(path)
        assert not result['errors'],(name,result['errors'])


def test_groundcover_is_dense_bounded_and_keeps_arrivals_and_course_recovery_clear():
    import hashlib
    data=L.FOLIAGE;chunks=data['chunks'];cell=data['cellSize']
    assert cell==2048 and 200<len(chunks)<650
    assert sum(len(c['grass']) for c in chunks)>240000
    assert sum(len(c['fern']) for c in chunks)>1400
    protected=[]
    for t in L.TELEPORTS:
        protected.extend((p[0],p[1],150) for p in t['arrivals'])
    for a in L.AREAS:
        if a.kind=='movementCourse':
            protected.append((*a.data['start'][:2],220))
            protected.extend((*s['recovery'][:2],220) for s in a.data.get('sections',[]))
    for chunk in chunks:
        ox,oy,z=chunk['origin'];assert z==0
        for kind in ('grass','fern'):
            for dx,dy,yaw,width,height,tone in chunk[kind]:
                assert 0<=dx<cell and 0<=dy<cell
                assert 0<=yaw<360 and 20<=height<=82 and 100<=width<=190 and 0<=tone<100
                x,y=ox+dx,oy+dy
                assert all(abs(x-px)>r or abs(y-py)>r for px,py,r in protected)
                for lake in L.LAKES:
                    cx,cy=lake['centre'];rx,ry=lake['radii'];ny=(y-cy)/ry;nx=(x-cx-rx*.13*ny)/rx
                    assert nx*nx+ny*ny>1.06**2
    sources={r['asset']:r for r in json.loads((HERE/'assets/sources.json').read_text())}
    grass=HERE/'assets'/data['grassTexture']
    assert hashlib.sha256(grass.read_bytes()).hexdigest()==sources[grass.stem]['sha256']
    assert sources[grass.stem]['license']=='MIT'
    assert 'Copyright (c) 2023 IcterusGames' in (HERE/'assets/SimpleGrassTextured-MIT.txt').read_text()


def test_every_scenery_instance_has_matching_collision_and_tree_climbs():
    import hashlib
    models=json.loads((HERE/'assets/collision.json').read_text())
    collision=G.C.read_collision((G.DEFAULT_OUT/'sim/collision.bin').read_bytes())
    sources={collision.strings[s.source] for s in collision.sources}
    ges={g.id:g for g in L.GES}
    for entry in L.SCENERY:
        assert entry['collision']==entry['id'] and entry['id'] in sources
        assert hashlib.sha256((HERE/'assets'/entry['asset']).read_bytes()).hexdigest()==models[entry['asset']]['sourceSha256']
        if '-tree-' in entry['asset']:
            assert len(entry['ges'])>=1,(entry['id'],'missing climb')
            assert all(ges[g].kind=='PIPE' for g in entry['ges'])
        assert all(g in ges for g in entry['ges'])
        for ident in entry['ges']:
            ge=ges[ident]
            if ge.kind!='PIPE':continue
            a,b,side=ge.points;n=[(side[k]-a[k])/10 for k in range(3)]
            for z in range(110,int(b[2]-a[2]-90),16):
                at=(a[0]+n[0]*37,a[1]+n[1]*37,a[2]+z)
                assert not L.MESH_CONTACTS.hits(at,(35,35,86),only=entry['id']),(ident,'blocked climb capsule')


def test_clear_rectangular_ledges_have_full_edge_to_edge_coverage():
    by_line={}
    for g in L.GES:
        if g.kind!='LG':continue
        a,b=g.points
        if a[2]!=b[2]:continue
        for axis in range(2):
            if a[1-axis]==b[1-axis]:
                by_line.setdefault((axis,a[1-axis],a[2]),[]).append(sorted((a[axis],b[axis])))
    checked=0
    for s in L.SOLIDS:
        if s.up or s.hi[2]<32:continue
        if s.material=='fence':
            assert any(g.kind=='FENCE' and abs(g.points[0][2]-s.hi[2])<.01 and
                       all(s.lo[k]-2<=p[k]<=s.hi[k]+2 for p in g.points for k in range(2)) for g in L.GES),s.name
            continue
        x0,y0,_=s.lo;x1,y1,z=s.hi
        for a,b in (((x0,y0,z),(x1,y0,z)),((x1,y1,z),(x0,y1,z)),((x0,y1,z),(x0,y0,z)),((x1,y0,z),(x1,y1,z))):
            if math.dist(a,b)<=50:continue
            report=L.ledge_report(L.GE('probe','LG',(a,b),'audit'))
            if 'error' in report or not report['clear'] or not report['climb']:continue
            axis=0 if a[1]==b[1] else 1;start,end=sorted((a[axis],b[axis]))
            spans=sorted(by_line.get((axis,a[1-axis],z),[]));cursor=start
            for lo,hi in spans:
                if lo<=cursor+.01:cursor=max(cursor,hi)
            assert cursor>=end-.01,(s.name,a,b,'uncovered edge')
            # Native nearest selection uses file order for overlapping, equally near
            # GEs. An obsolete short edge must not mask the new full-span grab.
            ordered=by_line.get((axis,a[1-axis],z),[])
            for lo,hi in ordered:
                for probe in (lo+1,hi-1):
                    if not start+24.5<probe<end-24.5:continue
                    first=next((span for span in ordered if span[0]<=probe<=span[1]),None)
                    assert first is not None and min(probe-first[0],first[1]-probe)>=24.5,(s.name,probe,'short GE masks full edge')
            checked+=1
    assert checked>500 # the mixed routes replace thousands of repeated stepping stones


def test_report_routes_alternate_native_mechanics_and_keep_short_clean_run_targets():
    for area in L.AREAS:
        if area.kind!='movementCourse' or area.data.get('courseId')=='easy':continue
        data=area.data
        assert data['revision']==(4 if data['courseId']=='medium' else 3)
        assert data['cleanRunSeconds']==([150,240] if data['courseId']=='medium' else [240,360])
        route=data['playtestRoute']
        kinds={step['kind'] for step in route}
        assert {'jump','ledge' if data['courseId']=='medium' else 'wallkick','pipe','ladder','hoh','shimmy','roll','fence','zip','lift'}<=kinds
        assert len(route)<150
        last_obstacle=next(s for s in reversed(route) if s['kind']!='walk')
        assert last_obstacle['gate']==len(data['checkpoints'])-1
        assert route[-1]['gate'] is None and route[-1]['at']==data['finish']
        for index in range(len(data['sections'])):
            assert len({step['kind'] for step in route if step['section']==index})>=3


def test_outdoor_dressing_has_dense_woodland_and_real_shallow_lake_beds():
    assert sum('-tree-' in e['asset'] for e in L.SCENERY)>=700
    assert sum(len(g['trees'])>=15 for g in L.WOODLANDS)>=24
    assert len(L.LAKES)==3
    carves,solids=G.world_volumes()
    root=B.build(G.world_polys(),[],1,balance=.25)
    for lake in L.LAKES:
        x,y=lake['centre']
        rx,ry=lake['radii']
        for entry in L.SCENERY:
            px,py,_=entry['at'];dy=(py-y)/ry;dx=(px-x-rx*.13*dy)/rx
            assert dx*dx+dy*dy>1,(lake['id'],entry['id'],'model floats above lake bed')
        assert _truth(carves,solids,(x,y,-40)),lake['id']
        assert not _truth(carves,solids,(x,y,-110)),lake['id']
        assert B.classify_point(root,(x,y,-40)),(lake['id'],'lake must cut the world floor')
        assert not B.classify_point(root,(x,y,-110)),(lake['id'],'lake bed must collide')
        assert any(s.name==lake['id']+'Pier' for s in L.SOLIDS)
    assert len([s for s in L.SOLIDS if s.name.startswith('PavilionColumn')])==4
    assert all(any(s.name=='PavilionSignBeam'+str(dx) and s.lo[2]<=1536<=s.hi[2]
                   for s in L.SOLIDS) for dx in (-370,370))


def test_course_entrances_and_single_mounted_boards_are_beside_the_start():
    for area in L.AREAS:
        if area.kind!='movementCourse' or area.data['courseId']=='easy':continue
        data=area.data;ident=data['courseId'];start=data['start']
        travel=next(t for t in L.TELEPORTS if t['id']==ident)
        assert math.dist(start[:2],travel['station'][:2])<=1600
        assert math.dist(start[:2],data['leaderboardAnchor'][:2])<=1100
        assert data['leaderboardYaw']==49152 and data['leaderboardSize']==[576,480]
        assert ident+'BoardTitle' not in L.GROUPS
        assert ident+'SectionSign0' not in L.GROUPS
        panel=next(s for s in L.SOLIDS if s.name==ident+'EntranceBacking')
        assert abs((panel.lo[1]+panel.hi[1])/2-start[1])<160
