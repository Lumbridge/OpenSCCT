"""Derive compact collision meshes from the credited GLBs (offline authoring step).

Requires numpy and fast-simplification. Normal generation reads the tracked result.
Coordinates are normalized Godot/glTF coordinates, with the same bounds and foot pivot
as OutdoorScenery.cs. Alpha foliage remains soft; all opaque trunks/branches and props collide.
"""
import hashlib
import json
import struct
from pathlib import Path
import numpy as np
import fast_simplification
from collections import defaultdict

HERE = Path(__file__).resolve().parent

def edges(points,faces):
    """Boundary of upward surface patches; weld seams and merge collinear adjoining segments.

    Keeping the actual outline avoids GE lines crossing the holes in the well, between
    picnic seats, or across the open doorway of the house.
    """
    p=np.asarray(points);f=np.asarray(faces)
    n=np.cross(p[f[:,1]]-p[f[:,0]],p[f[:,2]]-p[f[:,0]])
    upward=n[:,1] > np.linalg.norm(n,axis=1)*.65
    occurrences=defaultdict(list)
    for face in f[upward]:
        for a,b in zip(face,np.roll(face,-1)):occurrences[tuple(sorted((int(a),int(b))))].append((int(a),int(b)))
    boundary={items[0] for items in occurrences.values() if len(items)==1}
    # glTF winding is reversed by the UE axis swap. LG orientation follows the exposed rim.
    merged=[]
    while boundary:
        a,b=min(boundary);boundary.remove((a,b));start=a
        while True:
            choices=sorted(y for x,y in boundary if x==b)
            if len(choices)!=1:break
            c=choices[0];u=p[b]-p[start];v=p[c]-p[b]
            if np.dot(u,v)<=0 or np.linalg.norm(np.cross(u,v)) > .025*np.linalg.norm(u)*np.linalg.norm(v):break
            boundary.remove((b,c));b=c
        if np.linalg.norm((p[b]-p[a])[[0,2]])>.04 and max(p[a,1],p[b,1])>.035:
            merged.append([p[a].tolist(),p[b].tolist()])
    return merged

def read(path):
    raw=path.read_bytes(); n=struct.unpack_from('<I',raw,12)[0]
    doc=json.loads(raw[20:20+n]); binary=raw[28+n:]
    def accessor(i):
        a=doc['accessors'][i];v=doc['bufferViews'][a['bufferView']]
        dtype={5120:'i1',5121:'u1',5122:'<i2',5123:'<u2',5125:'<u4',5126:'<f4'}[a['componentType']]
        width={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
        item=np.dtype(dtype).itemsize
        return np.ndarray((a['count'],width),dtype=dtype,buffer=binary,
            offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(v.get('byteStride',item*width),item)).copy()
    meshes=[]
    def visit(index,parent):
        node=doc['nodes'][index]
        if 'matrix' in node:matrix=np.array(node['matrix']).reshape((4,4),order='F')
        else:
            x,y,z,w=node.get('rotation',[0,0,0,1])
            matrix=np.eye(4)
            matrix[:3,:3]=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],
                [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],
                [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]]) @ np.diag(node.get('scale',[1,1,1]))
            matrix[:3,3]=node.get('translation',[0,0,0])
        matrix=parent@matrix
        if 'mesh' in node:
            for p in doc['meshes'][node['mesh']]['primitives']:
                if p.get('mode',4)!=4:raise ValueError('Only triangle models supported')
                points=accessor(p['attributes']['POSITION']) @ matrix[:3,:3].T+matrix[:3,3]
                faces=accessor(p['indices']).reshape((-1,3)) if 'indices' in p else np.arange(len(points)).reshape((-1,3))
                if np.linalg.det(matrix[:3,:3])<0:faces=faces[:,::-1]
                meshes.append((points,faces,doc.get('materials',[{}])[p.get('material',0)]))
        for child in node.get('children',[]):visit(child,matrix)
    for node in doc['scenes'][doc.get('scene',0)]['nodes']:visit(node,np.eye(4))
    # Godot merges transformed local mesh AABBs, not just transformed vertices.
    # These assets use rigid orthogonal import roots; their vertex and transformed bounds agree.
    allpoints=np.concatenate([m[0] for m in meshes]);lo=allpoints.min(axis=0);hi=allpoints.max(axis=0)
    pivot=np.array([(lo[0]+hi[0])/2,lo[1],(lo[2]+hi[2])/2])
    size=hi-lo
    return [((v-pivot)/size,f,m) for v,f,m in meshes],size,pivot

def prepare():
    result={}
    for path in sorted((HERE/'assets').glob('*.glb')):
        if path.stem=='fern_02':continue # soft, render-only fronds; terrain beneath remains solid
        parts,size,pivot=read(path);points=[];faces=[];count=0
        tree='-tree-' in path.stem
        for p,f,mat in parts:
            # Leaf cards aren't solid walls. Trunks and woody branches are taken from their actual mesh.
            if tree and mat.get('alphaMode','OPAQUE')!='OPAQUE':
                if path.stem!='pine-tree-2':continue
                # This low-poly tree shares one cutout material between trunk and leaf cards.
                f=f[np.max(np.abs(p[f][:,:,[0,2]]),axis=(1,2))<.085]
            points.extend(p.tolist());faces.extend((f+count).tolist());count+=len(p)
        if not faces:raise ValueError('No solid geometry: '+path.name)
        points,remap=np.unique(np.round(points,7),axis=0,return_inverse=True);faces=remap[np.array(faces)]
        used,remap=np.unique(faces,return_inverse=True);points=points[used];faces=remap.reshape((-1,3))
        budget=1200 if path.stem in ('old-house','old-well') else 400 if path.stem=='boulder_01' else 160 if tree else 320
        if path.stem=='birch-tree-1':budget=3200 # preserve the long trunk as well as its many disconnected branches
        if path.stem=='flower-1':budget=60
        if len(faces)>budget:
            source_points,source_faces=points,faces
            while True:
                points,faces=fast_simplification.simplify(source_points,source_faces.astype(np.int32),target_count=budget,agg=5)
                drift=max(np.max(np.abs(points.min(0)-source_points.min(0))),np.max(np.abs(points.max(0)-source_points.max(0))))
                if (tree and abs(points[:,1].min()-source_points[:,1].min())<.01) or (not tree and drift<.06) or path.stem=='flower-1':break
                budget=min(len(source_faces),budget*2)
                if budget==len(source_faces):points,faces=source_points,source_faces;break
        result[path.name]={'sourceSha256':hashlib.sha256(path.read_bytes()).hexdigest(),
            'size':size.tolist(),'pivot':pivot.tolist(),'vertices':np.round(points,7).tolist(),'triangles':faces.tolist(),
            'ledges':[] if tree or path.stem in ('flower-1','street_lamp_01') else edges(points,faces)}
        print(path.stem,len(points),'vertices',len(faces),'triangles', 'dimensions',np.round(size,3))
    (HERE/'assets'/'collision.json').write_text(json.dumps(result,separators=(',',':'))+'\n',encoding='utf8')

if __name__=='__main__':prepare()
