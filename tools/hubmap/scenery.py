"""Place credited outdoor models around the authored gameplay geometry."""
import math
import random
import json
from pathlib import Path

def dress(L):
    L.SCENERY=[]
    def model(asset,at,height,yaw=0,size=None):
        entry={'id':f'Scenery{len(L.SCENERY):03d}','asset':asset+'.glb','at':list(at),'height':height,'yaw':yaw}
        if size:entry['size']=list(size)
        L.SCENERY.append(entry)
    # Imported collision replaces these old blockout volumes as well as their schematic rendering.
    removed=[]
    for i,s in enumerate(L.SOLIDS):
        at=((s.lo[0]+s.hi[0])/2,(s.lo[1]+s.hi[1])/2,s.lo[2])
        if s.name.startswith(('Grove','GardenTree')):
            model('birch-tree-1' if i%3==0 else 'pine-tree-1' if i%2 else 'pine-tree-2',at,1000+(i%5)*100,i*137.5%360)
            removed.append(s.name)
        elif s.name.startswith('PlazaBench'):
            if 'Back' not in s.name:
                east_west=s.hi[0]-s.lo[0]>100
                model('painted_wooden_bench',at,96,0 if east_west else 90,size=(320,88,96))
            removed.append(s.name)
        elif s.name.startswith(('LoungeSofa','LoungeSide')):
            if 'Back' not in s.name and 'Arm' not in s.name:
                model('Sofa_01',at,104,90,size=(s.hi[1]-s.lo[1],128,104))
            removed.append(s.name)
        elif s.name.startswith('LoungeTable'):
            model('CoffeeTable_01',at,32,90,size=(256,96,32))
            removed.append(s.name)
    L.SOLIDS[:]=[s for s in L.SOLIDS if s.name not in removed]
    L.GES[:]=[g for g in L.GES if not any(g.id.startswith(n) for n in removed)]
    L.GROUPS.pop('OutdoorPines',None);L.GROUPS.pop('GardenTrees',None)
    lamp_positions=[]
    for light in L.LIGHTS:
        if light.id.startswith('Walk_'):
            if any(math.dist(light.location[:2],p)<480 for p in lamp_positions):continue
            model('street_lamp_01',(light.location[0],light.location[1],0),420)
            lamp_positions.append(light.location[:2])
    for name,group in L.GROUPS.items():
        if name.startswith('Walk_'):
            group.pieces[:]=[p for p in group.pieces if max(v[2] for v in p.corners)<=4]
            group.volumes[:]=[v for v in group.volumes if v[1][2]<=4]
    # Plantings frame paths and districts, away from starts, queues, arrivals and traversal stations.
    groves=((2600,6500),(10200,10200),(2500,11200),(-9200,18000),(-11000,-11500),
            (14700,19600),(14800,25000),(17300,16000),(28000,23000),(0,-6500))
    for grove,(x,y) in enumerate(groves):
        for k in range(7):
            a=k*2.399;r=240+k*170
            px=x+math.cos(a)*r;py=y+math.sin(a)*r
            model('birch-tree-1' if grove%3==0 else 'pine-tree-1' if k%2 else 'pine-tree-2',(px,py,0),1000+(k%4)*180,k*47)
        for k in range(10):
            a=k*2.399;r=500+k*80
            model('flower-1',(x+math.cos(a)*r,y+math.sin(a)*r,0),60+(k%3)*20,k*83)
    for i,(x,y) in enumerate(((-9200,-11700),(-6500,-11400),(-5200,20300),(0,10700))):
        model('wooden_picnic_table',(x,y,0),120,i*90)
    for i,(x,y) in enumerate(((13900,15400),(-11200,23500))):
        model('old-house',(x,y,0),800,90*i,size=(1800,1700,800))
    model('old-well',(12000,18000,0),240)
    # Architectural keeps replace the piles of stretched rocks under the courses.
    for wall in L.CASTLE_WALLS:
        model('castle-wall',wall['at'],wall['height'],wall['yaw'],size=wall['size'])
    for cx,cy in ((8640,18400),(24600,18100)):
        model('castle-tower-square',(cx,cy,0),2600,size=(1800,1800,2600))
        for side in (-1,1):
            model('castle-tower-square',(cx+side*1500,cy,0),1600,size=(900,900,1600))
            model('castle-wall',(cx+side*1050,cy,0),1000,size=(1200,400,1000))
    for k in range(6):
        model('old_military_crate',(-15000+k*400,3600,0),150,k*9,size=(220,200,150))
        model('Barrel_01',(-15000+k*400,3200,0),170,k*25)
    # Older groves predate the lakes. Move their planting onto the new banks,
    # retaining instance identities, rather than leaving roots over open water.
    for lake in L.LAKES:
        cx,cy=lake['centre'];rx,ry=lake['radii']
        relocated=[]
        for entry in L.SCENERY:
            x,y,z=entry['at'];dy=(y-cy)/ry;dx=(x-cx-rx*.13*dy)/rx
            if dx*dx+dy*dy>=1.12**2:continue
            angle=math.atan2(dy,dx)
            for attempt in range(40):
                a=angle+attempt*.19
                px=cx+1.2*rx*math.cos(a)+rx*.13*math.sin(a)
                py=cy+1.2*ry*math.sin(a)
                if abs(px-cx)<600 and py<cy:continue # clear the pier and its access
                if all(math.dist((px,py),p)>240 for p in relocated):break
            entry['at']=[round(px),round(py),z];relocated.append((px,py))
    # Dense, irregular woodland stands fill the spaces between districts. Reuse
    # the imported meshes/materials, with deterministic scale and rotation variety.
    # Keep whole canopy envelopes away from routes, activities and safe arrivals.
    rng=random.Random(48271)
    centres=((-14500,-6500),(-14000,-1000),(-14500,13000),(-16000,19500),(-9500,17000),
             (-6500,12000),(-1800,13000),(-1200,20000),(-1000,27000),(3500,27000),
             (13000,27000),(16000,22500),(16600,15000),(21000,27000),(27500,27000),
             (27000,10500),(17500,2200),(23500,0),(27500,-4500),(26000,-11000),
             (19000,-13500),(10000,-15000),(3000,-15000),(-4000,-14500),(-7000,-1000),
             (-1000,2000),(11000,-2000),(12500,-7000),(-8000,-6000),(2000,-2000))
    L.WOODLANDS=[]
    occupied=[tuple(e['at'][:2]) for e in L.SCENERY]
    def clear(x,y,margin=600):
        if not all(L.HALL_LO[k]+margin<p<L.HALL_HI[k]-margin for k,p in enumerate((x,y))):return False
        if any(a.lo[0]-margin<x<a.hi[0]+margin and a.lo[1]-margin<y<a.hi[1]+margin for a in L.AREAS):return False
        for t in L.TELEPORTS:
            tx,ty=t['station'][:2]
            if math.dist((x,y),(tx,ty))<1200:return False
            if min(6144,tx)-margin<x<max(6144,tx)+margin and abs(y-8192)<margin:return False
            if abs(x-tx)<margin and min(8192,ty)-margin<y<max(8192,ty)+margin:return False
        for lake in L.LAKES:
            cx,cy=lake['centre'];rx,ry=lake['radii']
            if ((x-cx)/(rx+margin+400))**2+((y-cy)/(ry+margin))**2<1:return False
        if any(math.dist((x,y),p)<420 for p in occupied):return False
        return not L._box_hits((x,y,600),(180,180,600))
    for grove,(cx,cy) in enumerate(centres):
        instances=[]
        for attempt in range(200):
            if len(instances)>=22:break
            angle=rng.uniform(0,math.tau);r=2200*math.sqrt(rng.random())
            x,y=round(cx+math.cos(angle)*r),round(cy+math.sin(angle)*r)
            if not clear(x,y):continue
            species='birch-tree-1' if grove%4==0 else 'pine-tree-1' if attempt%3 else 'pine-tree-2'
            model(species,(x,y,0),rng.randint(1050,1750),rng.randrange(360))
            instances.append(L.SCENERY[-1]['id']);occupied.append((x,y))
            if len(instances)%4==0:
                model('flower-1',(x+120,y+120,0),rng.randint(60,100),rng.randrange(360))
        L.WOODLANDS.append({'centre':[cx,cy],'trees':instances})
    for lake in L.LAKES:
        cx,cy=lake['centre'];rx,ry=lake['radii']
        # Shoreline clusters and picnic clearings make the water a destination.
        for k in range(18):
            a=k*math.tau/18
            x,y=cx+math.cos(a)*(rx+500)+rx*.13*math.sin(a),cy+math.sin(a)*(ry+500)
            if clear(x,y,220):
                model('birch-tree-1' if k%3==0 else 'pine-tree-2',(x,y,0),1100+k%4*170,k*47)
                occupied.append((x,y))
        model('wooden_picnic_table',(cx+640,cy-ry-400,0),120,90)

    # One final placement policy covers legacy plantings AND new woodland. Use
    # the entire rotated canopy footprint, rather than just a trunk-sized probe.
    dimensions=json.loads((Path(__file__).resolve().parents[2]/'fixtures'/'hub-assets'/'collision.json').read_text())
    L.TREE_CLEARANCE=[]
    planted=[]
    rigid_cells={}
    for group in L.GROUPS.values():
        for lo,hi,_ in group.volumes:
            if hi[2]-lo[2]<=4:continue
            for ix in range(math.floor(lo[0]/2048),math.floor(hi[0]/2048)+1):
                for iy in range(math.floor(lo[1]/2048),math.floor(hi[1]/2048)+1):
                    rigid_cells.setdefault((ix,iy),[]).append((lo,hi))
    def tree_clear(e,x,y):
        sx,sy,sz=dimensions[e['asset']]['size'];h=e['height']
        a=math.radians(e['yaw']);c,s=abs(math.cos(a)),abs(math.sin(a))
        hx=(c*sx+s*sz)*h/sy/2+96;hy=(s*sx+c*sz)*h/sy/2+96
        if not (L.HALL_LO[0]+hx<x<L.HALL_HI[0]-hx and L.HALL_LO[1]+hy<y<L.HALL_HI[1]-hy):return False
        if any(lo[0]-hx<x<hi[0]+hx and lo[1]-hy<y<hi[1]+hy for lo,hi,_ in L.FLOOR_ZONES):return False
        if any(a.lo[0]-hx<x<a.hi[0]+hx and a.lo[1]-hy<y<a.hi[1]+hy for a in L.AREAS):return False
        if any(abs(x-t['station'][0])<hx+600 and abs(y-t['station'][1])<hy+800 for t in L.TELEPORTS):return False
        if L._box_hits((x,y,h/2),(hx,hy,h/2)):return False
        # contacts.py promotes rigid render volumes to solids after dressing.
        # Include them here too, especially the lounge's wall-mounted screen.
        for ix in range(math.floor((x-hx)/2048),math.floor((x+hx)/2048)+1):
            for iy in range(math.floor((y-hy)/2048),math.floor((y+hy)/2048)+1):
                if any(lo[0]-hx<x<hi[0]+hx and lo[1]-hy<y<hi[1]+hy and lo[2]<h and hi[2]>0
                       for lo,hi in rigid_cells.get((ix,iy),[])):return False
        if any(math.dist((x,y),p)<420 for p in planted):return False
        for lake in L.LAKES:
            cx,cy=lake['centre'];rx,ry=lake['radii']
            if ((x-cx)/(rx+hx+400))**2+((y-cy)/(ry+hy))**2<1:return False
        for other in L.SCENERY:
            if other is e or '-tree-' in other['asset'] or other['asset']=='flower-1.glb':continue
            ox,oy,_=other['at'];w,d,_=other.get('size',(other['height']*2,other['height']*2,other['height']))
            if abs(x-ox)<hx+max(w,d)/2 and abs(y-oy)<hy+max(w,d)/2:return False
        return True
    rejected=[]
    for e in L.SCENERY:
        if '-tree-' not in e['asset']:continue
        if not tree_clear(e,*e['at'][:2]):
            origin=e['at'][:2]
            for attempt in range(1200):
                angle=attempt*2.399963;r=384+240*math.sqrt(attempt)
                px=round(origin[0]+math.cos(angle)*r);py=round(origin[1]+math.sin(angle)*r)
                if tree_clear(e,px,py):e['at']=[px,py,0];break
            else:rejected.append(e['id']);continue
        L.TREE_CLEARANCE.append(e['id'])
        planted.append(tuple(e['at'][:2]))
    L.SCENERY[:]=[e for e in L.SCENERY if e['id'] not in rejected]
