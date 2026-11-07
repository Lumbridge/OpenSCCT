"""Place credited outdoor models around the authored gameplay geometry."""
import math
import random

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
    for light in L.LIGHTS:
        if light.id.startswith('Walk_'):
            model('street_lamp_01',(light.location[0],light.location[1],0),420)
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
    # Scanned rock outcrops give the ruins and observatory foundations an organic outline.
    for district,(cx,cy,radius) in enumerate(((8640,18400,1050),(24600,18100,1400))):
        for k in range(12):
            a=k*math.pi/6;at=(cx+math.cos(a)*radius,cy+math.sin(a)*radius,0)
            model('boulder_01',at,1800+(k%3)*650,k*37,size=(1000,1200,1800+(k%3)*650))
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
