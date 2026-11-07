"""Deterministic soft ground cover, batched spatially and kept off usable surfaces.

Grass and fern fronds bend rather than obstruct players. Rigid scenery, terrain and
all traversal surfaces retain their collision/GE authoring in contacts.py.
"""
import math
import random
from collections import defaultdict

CELL = 2048
PITCH = 76


def clearance(L):
    # Index rectangles so hundreds of thousands of plants need only local queries.
    blocked = defaultdict(list)
    def rect(lo, hi, margin=90):
        box = (lo[0]-margin, lo[1]-margin, hi[0]+margin, hi[1]+margin)
        for x in range(math.floor(box[0]/CELL), math.floor(box[2]/CELL)+1):
            for y in range(math.floor(box[1]/CELL), math.floor(box[3]/CELL)+1):
                blocked[x,y].append(box)
    for lo,hi,material in L.FLOOR_ZONES:
        if material not in ('turf','ruin_moss','roof'):rect(lo,hi)
    for s in L.SOLIDS:
        if s.lo[2] < 110 and s.hi[2] > 0:rect(s.lo,s.hi,120)
    for group in L.GROUPS.values():
        if any(group.name==lake['id']+'Water' for lake in L.LAKES):continue # curved shore checked below
        for p in group.pieces:
            if p.material=='water':
                rect(tuple(min(v[k] for v in p.corners) for k in range(2)),
                     tuple(max(v[k] for v in p.corners) for k in range(2)),80)
    for t in L.TELEPORTS:
        x,y=t['station'][:2];rect((x-450,y-750),(x+450,y+420),120)
    for a in L.AREAS:
        if a.kind=='movementCourse':
            for p in [a.data['start'],*[s['recovery'] for s in a.data.get('sections',[])]]:
                x,y=p[:2];rect((x-240,y-240),(x+240,y+240),80)
    for e in L.SCENERY:
        if '-tree-' in e['asset'] or e['asset']=='flower-1.glb':continue
        sx,sy,sz=e['sourceSize'];h=e['height']
        size=e.get('size',(h*sx/sy,h*sz/sy,h))
        angle=math.radians(e['yaw']);c,s=abs(math.cos(angle)),abs(math.sin(angle))
        hx=(size[0]*c+size[1]*s)/2;hy=(size[0]*s+size[1]*c)/2
        x,y=e['at'][:2];rect((x-hx,y-hy),(x+hx,y+hy),80)
    def clear(x,y):
        if any(x0<x<x1 and y0<y<y1 for x0,y0,x1,y1 in blocked.get((math.floor(x/CELL),math.floor(y/CELL)),())):return False
        for lake in L.LAKES:
            cx,cy=lake['centre'];rx,ry=lake['radii']
            dy=(y-cy)/ry;dx=(x-cx-rx*.13*dy)/rx
            if dx*dx+dy*dy < 1.065**2:return False
        return True
    return clear


def build(L):
    rng=random.Random(93417)
    clear=clearance(L)
    chunks={}
    def add(kind,x,y,yaw,width,height,tone):
        x,y=round(x,1),round(y,1) # quantize before choosing a cell at shared boundaries
        key=(math.floor(x/CELL),math.floor(y/CELL))
        chunk=chunks.setdefault(key,{'origin':[key[0]*CELL,key[1]*CELL,0],'grass':[],'fern':[]})
        # Cell-local uu: x,y,yaw,width,height,tone. Feet are exactly on the ground.
        chunk[kind].append([round(x-key[0]*CELL,1),round(y-key[1]*CELL,1),yaw,width,height,tone])
    trees=defaultdict(list)
    for e in L.SCENERY:
        if '-tree-' in e['asset']:
            x,y=e['at'][:2]
            for cx in range(math.floor((x-900)/CELL),math.floor((x+900)/CELL)+1):
                for cy in range(math.floor((y-900)/CELL),math.floor((y+900)/CELL)+1):trees[cx,cy].append((x,y))
    for ix in range(int(L.HALL_LO[0])+180,int(L.HALL_HI[0])-180,PITCH):
        for iy in range(int(L.HALL_LO[1])+180,int(L.HALL_HI[1])-180,PITCH):
            x=ix+rng.uniform(-PITCH*.44,PITCH*.44);y=iy+rng.uniform(-PITCH*.44,PITCH*.44)
            if not clear(x,y):continue
            # Broad irregular meadow patches, with dense undergrowth under trees.
            wooded=any((x-tx)**2+(y-ty)**2<850**2 for tx,ty in trees.get((math.floor(x/CELL),math.floor(y/CELL)),()))
            patch=(math.sin(x/530)+math.sin(y/710)+math.sin((x+y)/360))/3
            if rng.random()>(.98 if wooded else .78+.18*patch):continue
            near_plaza=math.hypot(x-6144,y-6144)<4000
            height=rng.randint(32,62) if not near_plaza else rng.randint(22,37)
            add('grass',x,y,rng.randrange(360),rng.randint(106,162),height,rng.randrange(100))
            if wooded and rng.random()<.016 and clear(x-120,y-120) and clear(x+120,y+120):
                add('fern',x,y,rng.randrange(360),rng.randint(110,190),rng.randint(48,82),rng.randrange(100))
    L.FOLIAGE={'format':1,'cellSize':CELL,'grassTexture':'grassbushcc008.png','fernAsset':'fern_02.glb',
               'chunks':[chunks[k] for k in sorted(chunks)]}
