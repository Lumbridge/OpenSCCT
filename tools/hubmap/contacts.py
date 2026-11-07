"""Physical scenery and traversal coverage. No rigid prop is a render-only placement."""
import json
import math
from dataclasses import replace
from pathlib import Path

def build(L):
    data=json.loads((Path(__file__).parent/'assets'/'collision.json').read_text())
    L.SCENERY_COLLISION=[]
    L.CONTACT_AUDIT=[]
    for group in L.GROUPS.values():
        if group.kit and group.kit[1]!='hub':continue
        for index,(lo,hi,material) in enumerate(group.volumes):
            if hi[2]-lo[2]<=4:continue # paint and light strips share the underlying surface
            if any(all(s.lo[k]-4<=lo[k] and s.hi[k]+4>=hi[k] for k in range(3)) for s in L._near_solids(lo,hi)):continue
            key='physical_'+material
            L.MATERIALS[key]=replace(L.MATERIALS[material],key=key,hidden=True)
            L.solid('Physical_'+group.name+'_'+str(index),lo,hi,key)
            L._solid_cells=None
    for entry in L.SCENERY:
        model=data[entry['asset']];sx,sy,sz=model['size'];height=entry['height']
        entry['sourceSize']=model['size'];entry['sourcePivot']=model['pivot']
        size=entry.get('size',(height*sx/sy,height*sz/sy,height))
        angle=math.radians(entry['yaw']);c=math.cos(angle);s=math.sin(angle)
        def transform(v):
            x,y,z=v[0]*size[0],v[2]*size[1],v[1]*height
            return (entry['at'][0]+c*x+s*y,entry['at'][1]-s*x+c*y,entry['at'][2]+z)
        vertices=[transform(v) for v in model['vertices']]
        L.SCENERY_COLLISION.append({'id':entry['id'],'asset':entry['asset'],'vertices':vertices,
            'triangles':[(a,c,b) for a,b,c in model['triangles']]})
        entry['collision']=entry['id'];entry['ges']=[]
        def ge(kind,points,label):
            ident=entry['id']+label
            L.ge(ident,kind,*points,note='model surface: '+entry['asset'])
            entry['ges'].append(ident)
        if '-tree-' in entry['asset'] or entry['asset']=='street_lamp_01.glb':
            trunk=[v for v in vertices if entry['at'][2]-2<=v[2]<entry['at'][2]+height*.28]
            if not trunk:raise ValueError('Tree has no physical trunk: '+entry['asset'])
            low=[min(v[k] for v in trunk) for k in range(2)];high=[max(v[k] for v in trunk) for k in range(2)]
            mid=[(low[k]+high[k])/2 for k in range(2)]
            for axis in range(2):
                for sign in (-1,1):
                    at=[*mid,entry['at'][2]];at[axis]=(low if sign<0 else high)[axis]
                    top=at.copy();top[2]+=max(320,height*.55)
                    side=at.copy();side[axis]+=sign*10
                    ge('PIPE',[at,top,side],f'Climb{axis}_{"N" if sign<0 else "P"}')
        else:
            for k,(a,b) in enumerate(model['ledges']):
                a,b=transform(a),transform(b)
                if math.dist(a[:2],b[:2])<=50 or abs(a[2]-b[2])>math.dist(a[:2],b[:2])*.5:continue
                ge('LG',[b,a],'Rim'+str(k))
        L.CONTACT_AUDIT.append({'id':entry['id'],'asset':entry['asset'],'collision':True,'ges':entry['ges'],
            'note':'soft foliage; solid trunk/branches' if '-tree-' in entry['asset'] else 'mesh surface'})
    # Beams, cables, rung faces and standalone signs are polygons rather than boxes.
    # Give any rigid face not already covered by a solid its own spatial mesh collision.
    # Decals/emissive strips and water are surface effects, not additional physical floors.
    chunks={}
    for group in L.GROUPS.values():
        if group.kit and group.kit[1]!='hub':continue
        for piece in group.pieces:
            lo=tuple(min(p[k] for p in piece.corners) for k in range(3));hi=tuple(max(p[k] for p in piece.corners) for k in range(3))
            sign=piece.material.startswith('art_') and hi[2]-lo[2]>8
            if (L.MATERIALS[piece.material].unlit and not sign) or piece.material=='water':continue
            if any(all(s.lo[k]-4<=lo[k] and s.hi[k]+4>=hi[k] for k in range(3)) for s in L._near_solids(lo,hi)):continue
            centre=tuple((lo[k]+hi[k])/2 for k in range(3))
            key=(*[math.floor(v/4096) for v in centre],piece.material)
            mesh=chunks.setdefault(key,{'id':'Details_'+str(len(chunks)),'asset':'authored','material':piece.material,'vertices':[],'triangles':[]})
            start=len(mesh['vertices']);mesh['vertices'].extend(piece.corners)
            mesh['triangles'].extend((start,start+k,start+k+1) for k in range(1,len(piece.corners)-1))
    L.SCENERY_COLLISION.extend(chunks.values())
    # Complete exposed rectangular perimeters. Split at physical intersections rather
    # than arbitrarily shortening ledge ends. Retain existing authored GE identities.
    original=list(L.GES)
    for solid in list(L.SOLIDS):
        if solid.up or solid.hi[2]<32:continue
        # A fence's full-span FENCE GE owns its top transition. A nearer LG on
        # either physical face would mask FenceTop in the native nearest query.
        if solid.material=='fence':continue
        x0,y0,_=solid.lo;x1,y1,z=solid.hi
        for face,a,b in (('S',(x0,y0,z),(x1,y0,z)),('N',(x1,y1,z),(x0,y1,z)),
                         ('W',(x0,y1,z),(x0,y0,z)),('E',(x1,y0,z),(x1,y1,z))):
            axis=0 if face in 'SN' else 1;length=abs(b[axis]-a[axis]);direction=1 if b[axis]>a[axis] else -1
            if length<=50:continue
            cuts={0.,length};covered=[]
            for g in original:
                if g.kind!='LG':continue
                p,q=g.points
                if p[2]==q[2]==z and p[1-axis]==q[1-axis]==a[1-axis]:
                    start,end=sorted(((p[axis]-a[axis])*direction,(q[axis]-a[axis])*direction))
                    if end>0 and start<length:
                        covered.append((max(0,start),min(length,end)));cuts.update((max(0,start),min(length,end)))
            full=L.GE('Auto_'+solid.name.replace('-','Neg')+face+'Full','LG',(a,b),'exposed edge, full physical span')
            report=L.ledge_report(full)
            if 'error' not in report and report['clear'] and report['climb']:
                if not any(u<=0 and v>=length for u,v in covered):L.GES.append(full)
                continue
            lo=(min(a[0],b[0])-80,min(a[1],b[1])-80,z-210)
            hi=(max(a[0],b[0])+80,max(a[1],b[1])+80,z+190)
            for other in L._near_solids(lo,hi):
                if other.name==solid.name or other.hi[2]<z-210 or other.lo[2]>z+190:continue
                for v in (other.lo[axis],other.hi[axis]):
                    t=(v-a[axis])*direction
                    if 0<t<length:cuts.add(t)
            cuts=sorted(cuts)
            spans=[]
            for k,(start,end) in enumerate(zip(cuts,cuts[1:])):
                if end-start<=50 or any(u<=start and v>=end for u,v in covered):continue
                p=list(a);q=list(a);p[axis]+=direction*start;q[axis]+=direction*end
                candidate=L.GE('Auto_'+solid.name.replace('-','Neg')+face+str(k),'LG',(tuple(p),tuple(q)),'exposed edge, full physical span')
                report=L.ledge_report(candidate)
                if 'error' not in report and report['clear'] and report['climb']:
                    if spans and abs(spans[-1][1]-start)<.01:spans[-1]=(spans[-1][0],end)
                    else:spans.append((start,end))
            for k,(start,end) in enumerate(spans):
                p=list(a);q=list(a);p[axis]+=direction*start;q[axis]+=direction*end
                candidate=L.GE('Auto_'+solid.name.replace('-','Neg')+face+str(k),'LG',(tuple(p),tuple(q)),'exposed edge, full physical span')
                report=L.ledge_report(candidate)
                if 'error' not in report and report['clear'] and report['climb']:L.GES.append(candidate)
    # Native selection breaks equal-distance ties in file order. Extend an older short
    # GE to its covering full edge, then deduplicate it; otherwise the old endpoint's
    # no-grab margin would mask a perfectly usable part of the new perimeter.
    lines={}
    for g in L.GES:
        if g.kind!='LG' or g.id.startswith('Scenery'):continue
        a,b=g.points
        if a[2]!=b[2]:continue
        axis=0 if a[1]==b[1] else 1 if a[0]==b[0] else -1
        if axis>=0:lines.setdefault((axis,a[1-axis],a[2]),[]).append((min(a[axis],b[axis]),max(a[axis],b[axis])))
    normalized=[];seen=set()
    # Adjacent gallery slabs overlap at their joins. Their collinear, clear
    # handholds form one physical edge, with no artificial no-grab seam.
    for key,spans in lines.items():
        merged=[]
        axis,other,z=key
        for lo,hi in sorted(spans):
            if merged and lo<=merged[-1][1]+.01:
                begin=merged[-1][0];end=max(hi,merged[-1][1])
                a=[other,other,z];b=a.copy();a[axis]=begin;b[axis]=end
                report=L.ledge_report(L.GE('join_probe','LG',(tuple(a),tuple(b)),'joined edge'))
                if 'error' not in report and report['clear'] and report['climb']:
                    merged[-1]=(begin,end);continue
            merged.append((lo,hi))
        lines[key]=merged+spans
    for g in L.GES:
        if g.kind=='LG' and not g.id.startswith('Scenery'):
            a,b=g.points;axis=0 if a[1]==b[1] else 1 if a[0]==b[0] else -1
            if axis>=0 and a[2]==b[2]:
                key=(axis,a[1-axis],a[2]);lo,hi=sorted((a[axis],b[axis]))
                lo,hi=max((span for span in lines[key] if span[0]<=lo and span[1]>=hi),key=lambda span:span[1]-span[0])
                identity=(*key,lo,hi)
                if identity in seen:continue
                seen.add(identity)
                p=list(a);q=list(b);p[axis],q[axis]=(lo,hi) if b[axis]>a[axis] else (hi,lo)
                g=replace(g,points=(tuple(p),tuple(q)))
        normalized.append(g)
    L.GES[:]=normalized
    L.MODEL_GE_IDS={g for entry in L.SCENERY for g in entry['ges']}
    # A GE ends before a branch blocks the native climb capsule. Four sides can have
    # different useful heights; a climber never has to push through solid branches.
    import importlib.util
    spec=importlib.util.spec_from_file_location('hub_mesh_contacts',Path(__file__).with_name('mesh_contacts.py'))
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    L.MESH_CONTACTS=module.MeshContacts(L.SCENERY_COLLISION)
    # Keep rims with a real native approach: high rims can be used for hanging/shimmying
    # even when a sloping roof prevents pulling up. Low rims need a clear pull-up.
    # Scanned seams and narrow ornamental strips must not become phantom grab prompts.
    rejected=set()
    for g in L.GES:
        if g.id not in L.MODEL_GE_IDS or g.kind!='LG':continue
        a,b=g.points;length=math.dist(a[:2],b[:2]);nx=(b[1]-a[1])/length;ny=-(b[0]-a[0])/length
        if not all(L.MESH_CONTACTS.supported(tuple(a[k]+(b[k]-a[k])*t for k in range(3)),(nx,ny)) for t in (.2,.5,.8)):
            rejected.add(g.id);continue
        clear_sides={-1,1}
        inset=24.5+35
        samples=[.5] if length<=2*inset else [inset/length,.5,1-inset/length]
        for t in samples:
            p=tuple(a[k]+(b[k]-a[k])*t for k in range(3))
            for sign in tuple(clear_sides):
                out=(nx*sign,ny*sign)
                if p[2]>=205:
                    front=(p[0]+39*out[0],p[1]+39*out[1],p[2]-117);ext=(35,35,87.5)
                else:
                    half=(p[2]+137.5)/2
                    front=(p[0]+39*out[0],p[1]+39*out[1],half+.5);ext=(35,35,half)
                top=(p[0]-36.7*out[0],p[1]-36.7*out[1],p[2]+51.6)
                stand=(p[0]-18.8*out[0],p[1]-18.8*out[1],p[2]+93.8)
                raised=(front[0],front[1],front[2]+50)
                pull_up=(not L.MESH_CONTACTS.hits(top,(35,35,50)) and not L._box_hits(top,(35,35,50))) or (
                    not L.MESH_CONTACTS.hits(stand,(35,35,87.5)) and not L._box_hits(stand,(35,35,87.5)))
                if (L.MESH_CONTACTS.hits(front,ext) or L._box_hits(front,ext) or
                    L.MESH_CONTACTS.hits(raised,ext) or L._box_hits(raised,ext) or
                    (p[2]<205 and not pull_up)):clear_sides.remove(sign)
            if not clear_sides:break
        if not clear_sides:rejected.add(g.id)
    L.GES[:]=[g for g in L.GES if g.id not in rejected]
    for entry in L.SCENERY:entry['ges'][:]=[g for g in entry['ges'] if g not in rejected]
    L.MODEL_GE_IDS-=rejected
    by_id={g.id:i for i,g in enumerate(L.GES)}
    for entry in L.SCENERY:
        for ident in entry['ges']:
            index=by_id[ident];g=L.GES[index]
            if g.kind!='PIPE':continue
            a,b,side=g.points;n=[(side[k]-a[k])/10 for k in range(3)]
            top=b[2]
            for z in range(110,int(b[2]-a[2]-90),16):
                at=(a[0]+n[0]*37,a[1]+n[1]*37,a[2]+z)
                if L.MESH_CONTACTS.hits(at,(35,35,86),only=entry['id']):
                    top=min(top,a[2]+z+90-20);break
            if top>=a[2]+220:L.GES[index]=replace(g,points=(a,(b[0],b[1],top),side))
            else:rejected.add(g.id)
    L.GES[:]=[g for g in L.GES if g.id not in rejected]
    for entry in L.SCENERY:entry['ges'][:]=[g for g in entry['ges'] if g not in rejected]
    L.MODEL_GE_IDS-=rejected
