"""Deterministic expanded Hub districts and authored course motifs, dressed with credited scenery.

Routes are sampled from architectural contours, with explicit section landings, hidden validation gates and
three server-driven movers. Dimensions remain spy-sized; only the space between districts grows.
"""
from __future__ import annotations

import math
from dataclasses import replace


def expand(L):
    L.TELEPORTS = []
    L.MOVERS = []
    L.ROUTE_STEPS = []
    L.FOG.update(color=[146,171,184], start=16000.0, end=60000.0)
    L.ZONE_AMBIENT = 104
    L.MATERIALS["skyline"] = replace(L.MATERIALS["skyline"], hidden=True)
    L.MATERIALS["sky"] = replace(L.MATERIALS["sky"], hidden=True)
    L.MATERIALS["roof"] = replace(L.MATERIALS["roof"], art="turf", tile=(768,768))
    for a in L.ACTORS:
        if a.id == "ZoneHall": a.props["AmbientBrightness"] = L.ZONE_AMBIENT
    for i,a in enumerate(L.AREAS):
        if a.kind == "movementCourse":
            L.AREAS[i] = replace(a, title="Easy - Training Grounds", data={**a.data, "courseId":"easy", "revision":4, "maxSeconds":300})
    for key,art,surface in (("ruin_stone","museum_marble",5),("ruin_moss","turf",5),("ruin_wood","wood",17),
                             ("frozen_stone","polar_frost",5),("frozen_ice","polar_ice",5),("observatory","lab_panel",14)):
        L.mat(key, art=art, surface=surface)

    def floor(lo, hi, material): L.floor_zone(lo,hi,material)
    def slab(name, at, half, material, thick=64):
        x,y,z=at
        L.solid(name,(x-half[0],y-half[1],max(0,z-thick)),(x+half[0],y+half[1],z),material)
        return L.SOLIDS[-1]
    def sign(name, at, title, subtitle, colour=(150,220,255), width=768, normal=None, base=0):
        normal=normal or L.S
        key="art_"+name.lower()
        L.art_mat(key,"sign",[title,subtitle],768,192,fg=colour)
        L.wall_sign(name,at,width,width/4,normal,key)
        # The printed face belongs to a timber notice board, with real posts.
        x,y,z=at;nx,ny=normal;rx,ry=-ny,nx;h=width/4
        def box(label,r0,r1,n0,n1,z0,z1,material):
            pts=[(x+rx*r+nx*n,y+ry*r+ny*n,q) for r in (r0,r1) for n in (n0,n1) for q in (z0,z1)]
            L.solid(name+label,tuple(min(p[k] for p in pts) for k in range(3)),
                    tuple(max(p[k] for p in pts) for k in range(3)),material)
        box('Backing',-width/2,width/2,-20,-1,z-h/2,z+h/2,'darkpanel')
        for side in (-1,1):
            r=side*(width/2+12)
            box('Post'+str(side),r-12,r+12,-24,0,base,z+h/2+24,'wood')
        box('Cap',-width/2-32,width/2+32,-28,8,z+h/2,z+h/2+24,'wood')
    def path(name, a, b, width=640, material="plaza"):
        # Orthogonal, level pedestrian paths never enter a scored route above them.
        x0,y0=a; x1,y1=b
        if x0 != x1:
            floor((min(x0,x1),y0-width/2),(max(x0,x1),y0+width/2),material)
            for side in (-1,1): L.box(name,(min(x0,x1),y0+side*width/2-3,1),(max(x0,x1),y0+side*width/2+3,3),"neon_teal",faces="t")
        if y0 != y1:
            floor((x1-width/2,min(y0,y1)),(x1+width/2,max(y0,y1)),material)
            for side in (-1,1): L.box(name,(x1+side*width/2-3,min(y0,y1),1),(x1+side*width/2+3,max(y0,y1),3),"neon_teal",faces="t")
        distance=math.dist(a,b)
        for k in range(1,max(2,int(distance/2048))):
            t=k/max(2,int(distance/2048));x=x0+(x1-x0)*t;y=y0+(y1-y0)*t
            # Lighting follows a route through its corner, not the diagonal shortcut.
            if abs(x1-x0)>abs(y1-y0):y=y0+width/2+96
            else:x=x1+width/2+96
            L.box(name,(x-8,y-8,0),(x+8,y+8,384),"metal")
            L.box(name,(x-20,y-20,360),(x+20,y+20,396),"neon_teal")
            L.light(name+"Light"+str(k),(x,y,400),110,85,hue=140,saturation=200)

    def station(ident,title,at,yaw=0):
        x,y,z=at
        # The marked station is beside the arrivals; none of these points arms a course.
        L.box("Teleport_"+ident,(x-288,y-288,z+0.5),(x+288,y+288,z+4),"neon_cyan",faces="t")
        for side in (-1,1):
            L.solid("Teleport_"+ident+str(side),(x+side*320-24,y+240,z),(x+side*320+24,y+288,z+512),"darkpanel")
            L.box("TeleportGlow_"+ident,(x+side*320-5,y+236,z+24),(x+side*320+5,y+239,z+488),"neon_cyan")
        sign("TravelSign_"+ident,(x,y+264,z+600),title,"TRAVEL DIRECTORY / HUD BUTTON")
        arrivals=[[x+dx,y-192+dy,z] for dy in (0,-144,-288,-432) for dx in (0,-144,144,-288,288)]
        L.TELEPORTS.append({"id":ident,"title":title,"station":[x,y,z+86],"arrivals":arrivals,"yaw":yaw})
        L.light("TravelLight_"+ident,(x,y,z+680),180,100,hue=130,saturation=170)

    # Existing districts are moved with their own geometry by layout.py.
    station("plaza","Central Plaza",(6144,8192,0),49152)
    station("range","Industrial Range",(-10240,6144,0),32768)
    station("garden","Training Garden",(-6144,21760,0),16384)
    station("matches","Match District",(22528,6144,0),0)
    station("lounge","Waterfront Lounge",(-8192,-10240,0),32768)
    station("easy","Easy - Training Grounds",(3328,-8704,0),49152)
    station("medium","Medium - Overgrown Ruins",(11008,14336,0),16384)
    station("hard","Hard - Frozen Observatory",(27264,13824,0),16384)
    for t in L.TELEPORTS[1:]:path("Walk_"+t["id"],(6144,8192),tuple(t["station"][:2]))

    # Outdoor groves between the districts. Original faceted pine canopies keep the horizon open.
    for grove,(gx,gy) in enumerate(((0,0),(-4096,12288),(0,18432),(14500,10500),(16000,-6000),
                                  (26000,-8000),(-7000,1000),(-14000,17000))):
        for tree in range(5):
            a=tree*2.399; r=240+tree*150; x=gx+math.cos(a)*r; y=gy+math.sin(a)*r
            ident=f"Grove{grove}Tree{tree}"
            L.solid(ident,(x-24,y-24,0),(x+24,y+24,400),"bark")
            for base,radius,top in ((300,240,680),(500,176,840)):
                for k in range(8):
                    a=k*math.pi/4;b=(k+1)*math.pi/4
                    L.piece("OutdoorPines",[(x+math.cos(a)*radius,y+math.sin(a)*radius,base),
                        (x+math.cos(b)*radius,y+math.sin(b)*radius,base),(x,y,top)],"foliage")
    L.box("GardenStream",(-3072,7680,1),(-1024,8704,3),"water",faces="t")
    L.solid("GardenBridgeWest",(-3328,7872,0),(-2560,8512,128),"wood",up="+x")
    L.solid("GardenBridgeDeck",(-2560,7872,64),(-1536,8512,128),"wood")
    L.solid("GardenBridgeEast",(-1536,7872,0),(-768,8512,128),"wood",up="-x")
    for y in (7880,8504):
        L.solid("BridgeRail"+str(y),(-2560,y-8,128),(-1536,y+8,224),"wood")

    # Ruins gardens, frost fields, industrial precinct and harbour water establish distinct terrain colours.
    floor((3072,14336),(14336,28672),"ruin_moss")
    floor((16384,12800),(29184,28672),"frozen_ice")
    floor((-15360,4096),(-9984,8704),"steel_concrete")
    floor((22272,3840),(27904,8704),"lab_panel")
    # Reflecting water is fenced and visually recessed behind the lounge, not a walkable blue floor.
    L.box("HarbourWater",(-17408,-17408,1),(-9472,-12544,3),"water",faces="t")
    for k,(a,b) in enumerate((((-17408,-12544),(-9472,-12544)),((-9472,-17408),(-9472,-12544)))):
        L.railing("HarbourRail"+str(k),a,b)
    for i in range(7):
        x=-16896+i*1024
        L.box("HarbourBoats",(x,-15616,4),(x+480,-14592,160),"wood")
        L.box("HarbourBoats",(x+176,-15104,160),(x+304,-14976,800),"metal")
    # An industrial silhouette behind the range, with an open entrance facing its station.
    for i in range(4):
        x=-14848+i*896
        L.solid("RangeSilo"+str(i),(x,9000,0),(x+512,9512,1800+i*160),"steel_corrugated")
        L.box("RangeSiloBand",(x-4,8996,1400),(x+516,9516,1440),"neon_sodium")
    for i in range(6):
        L.box("MatchPylons",(28160,4096+i*768,0),(28416,4352+i*768,2304),"lab_panel")
        L.box("MatchPylons",(28152,4104+i*768,512),(28160,4344+i*768,2200),"neon_lab")

    def mover(ident,start,end,material,seconds=6):
        delta=tuple(end[i]-start[i] for i in range(3)); half=(176,176,32)
        L.MOVERS.append({"id":ident,"at":start,"end":end,"half":half,"material":material})
        vec=lambda p:dict(zip(("X","Y","Z"),map(float,p)))
        L.actor(ident,"Engine.Mover",start,BasePos=vec(start),InitialState="TriggerPound",NumKeys=2,
                KeyPos={"$elements":{"0":vec((0,0,0)),"1":vec(delta)}},MoveTime=float(seconds),OtherTime=3.0,StayOpenTime=3.0,
                MoverEncroachType=0,StaticMesh={"$asset":"Hub."+ident},DrawType=8,
                bCollideActors=True,bBlockActors=True,bBlockPlayers=True,bCollideWorld=True,Tag="HubCourseMover",
                bAlwaysRelevant=True,NetUpdateFrequency=4.0)

    import importlib.util
    from pathlib import Path
    spec = importlib.util.spec_from_file_location('hub_courses', Path(__file__).with_name('courses.py'))
    courses = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(courses)
    courses.build(L, sign, path, mover)
