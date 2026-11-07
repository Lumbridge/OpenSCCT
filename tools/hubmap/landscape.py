"""Open plaza architecture and shallow, physically carved woodland lakes."""
import math


def build(L):
    x,y=6144,6144
    # Reuse the four existing pylon footprints: no new obstruction in spawn rings.
    for i,(px,py) in enumerate(((4800,4800),(7488,4800),(4800,7488),(7488,7488))):
        L.solid('PavilionColumn'+str(i),(px-64,py-64,544),(px+64,py+64,1536),'wood')
        for direction in (-1,1):
            L.beam('PavilionBraces',(px,py,1120),(px+direction*320,py,1472),28,'wood')
    for side in (-1,1):
        yy=y+side*1344
        L.solid('PavilionBeamY'+str(side),(x-1536,yy-48,1440),(x+1536,yy+48,1536),'wood')
        xx=x+side*1344
        L.solid('PavilionBeamX'+str(side),(xx-48,y-1536,1440),(xx+48,y+1536,1536),'wood')
    # Central crossbeams give the fountain sign's four cables a visible anchor.
    for dx in (-370,370):
        L.solid('PavilionSignBeam'+str(dx),(x+dx-24,y-1344,1504),(x+dx+24,y+1344,1536),'wood')
    L.solid('PavilionRoofWest',(x-1568,y-1568,1536),(x,y+1568,2048),'ruin_wood',up='+x')
    L.solid('PavilionRoofEast',(x,y-1568,1536),(x+1568,y+1568,2048),'ruin_wood',up='-x')
    L.solid('PavilionRidge',(x-48,y-1600,2048),(x+48,y+1600,2080),'wood')
    # Broad level eaves remain real ledges, including continuous side grabs.
    for side in (-1,1):
        xx=x+side*1600
        L.solid('PavilionEave'+str(side),(xx-32,y-1600,1472),(xx+32,y+1600,1536),'wood')
    # Legacy district titles used to hang from the removed hall ceiling/walls.
    # Keep their printed faces but replace those cables with grounded frames.
    L.SIGN_FRAMES=[]
    for name in ('GardenSign','PadsSign','RangeSign','CourseSign','LoungeSign'):
        group=L.GROUPS[name]
        group.pieces[:]=[p for p in group.pieces if p.material.startswith('art_')]
        panels=[];processed=set()
        for p in group.pieces:
            if id(p) in processed:continue
            lo=[min(v[k] for v in p.corners) for k in range(3)]
            hi=[max(v[k] for v in p.corners) for k in range(3)]
            centre=tuple((a+b)/2 for a,b in zip(lo,hi))
            if any(math.dist(centre,other)<2 for other in panels):continue
            panels.append(centre)
            axis=0 if hi[0]-lo[0]>hi[1]-lo[1] else 1
            fixed=1-axis
            # Cross product gives the first printed face's outward direction.
            a,b,c=p.corners[:3]
            u=[b[k]-a[k] for k in range(3)];v=[c[k]-a[k] for k in range(3)]
            n=(u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2])
            sign=1 if n[fixed]>0 else -1
            ident=name+'Frame'+str(len(panels))
            def solid(label,low,high):L.solid(ident+label,low,high,'wood')
            low=lo.copy();high=hi.copy();low[fixed]=centre[fixed]-8;high[fixed]=centre[fixed]+8
            solid('Board',low,high)
            for j,end in enumerate((lo[axis]-16,hi[axis]+16)):
                low=[*centre];high=[*centre]
                low[axis]=end-16;high[axis]=end+16
                low[fixed]=centre[fixed]+sign*32-16;high[fixed]=low[fixed]+32
                low[2]=0;high[2]=hi[2]+32
                solid('Post'+str(j),low,high)
            low=lo.copy();high=hi.copy()
            low[axis]-=48;high[axis]+=48
            low[fixed]=centre[fixed]-24;high[fixed]=centre[fixed]+48
            low[2]=hi[2];high[2]=hi[2]+32
            solid('Cap',low,high)
            # Place front/back printing on the actual board surfaces.
            for face in group.pieces:
                mid=tuple(sum(v[k] for v in face.corners)/len(face.corners) for k in range(3))
                if math.dist(mid,centre)>=2:continue
                processed.add(id(face))
                direction=sign if face is p else -sign
                face.corners=[tuple(centre[k]+direction*8.5 if k==fixed else v[k] for k in range(3)) for v in face.corners]
            L.SIGN_FRAMES.append(ident)
    L.LAKES=[]
    for ident,centre,radii in (('WillowLake',(-2600,-7000),(3900,2600)),
                               ('EastLake',(18500,-6600),(5000,3200)),
                               ('NorthLake',(-14000,26000),(2800,2200))):
        cx,cy=centre;rx,ry=radii
        # A many-sided, asymmetric convex shore, sloping to a shallow lake bed.
        ring=[]
        for k in range(32):
            a=k*math.tau/32
            ring.append((cx+rx*math.cos(a)+rx*.13*math.sin(a),cy+ry*math.sin(a)))
        L.LAKES.append({'id':ident,'centre':centre,'radii':radii,'shore':ring,'depth':96})
        water=[(cx+(px-cx)*.98,cy+(py-cy)*.98,-8) for px,py in ring]
        for k in range(len(water)):
            L.piece(ident+'Water',[(cx,cy,-8),water[k],water[(k+1)%len(water)]],'water')
        # The pier reaches beyond the bank. Solid deck/supports are grab-enabled
        # by the same complete-perimeter pass as course architecture.
        yy=cy-ry
        L.solid(ident+'Pier',(cx-160,yy-192,0),(cx+160,yy+896,64),'wood')
        L.solid(ident+'Landing',(cx-352,yy-384,0),(cx+352,yy-192,64),'wood')
        L.solid(ident+'Ramp',(cx-160,yy-896,0),(cx+160,yy-384,64),'wood',up='+y')
        for side in (-1,1):
            L.beam(ident+'PierPosts',(cx+side*144,yy+828,-96),(cx+side*144,yy+828,184),12,'wood')
