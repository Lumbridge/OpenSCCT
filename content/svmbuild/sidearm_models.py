"""Assemble authored pistol parts onto the player's converted merc arms without shipping retail geometry."""
from copy import deepcopy
from pathlib import Path
import json
import struct
import numpy as np

DTYPES={5121:np.uint8,5123:np.dtype('<u2'),5125:np.dtype('<u4'),5126:np.dtype('<f4')}
WIDTH={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}


def read(path):
    raw=path.read_bytes();size=struct.unpack_from('<I',raw,12)[0]
    return json.loads(raw[20:20+size]),bytearray(raw[28+size:])


def write(path,doc,binary):
    doc['buffers']=[{'byteLength':len(binary)}]
    js=json.dumps(doc,sort_keys=True,separators=(',',':')).encode();js+=b' '*(-len(js)%4)
    binary+=b'\0'*(-len(binary)%4)
    path.write_bytes(struct.pack('<III',0x46546c67,2,28+len(js)+len(binary))+struct.pack('<II',len(js),0x4e4f534a)+js+struct.pack('<II',len(binary),0x004e4942)+binary)


def values(doc,data,index):
    a=doc['accessors'][index];view=doc['bufferViews'][a['bufferView']]
    dtype=DTYPES[a['componentType']];width=WIDTH[a['type']]
    offset=view.get('byteOffset',0)+a.get('byteOffset',0)
    stride=view.get('byteStride',np.dtype(dtype).itemsize*width)
    return np.ndarray((a['count'],width),dtype=dtype,buffer=data,offset=offset,
        strides=(stride,np.dtype(dtype).itemsize)).copy()


def append(doc,data,array,kind,component=5126,target=None):
    data+=b'\0'*(-len(data)%4)
    array=np.asarray(array,dtype=DTYPES[component]);offset=len(data);data.extend(array.tobytes())
    view={'buffer':0,'byteOffset':offset,'byteLength':array.nbytes}
    if target:view['target']=target
    index=len(doc['bufferViews']);doc['bufferViews'].append(view)
    accessor={'bufferView':index,'componentType':component,'count':len(array),'type':kind}
    if kind=='VEC3':accessor.update(min=array.min(axis=0).tolist(),max=array.max(axis=0).tolist())
    index=len(doc['accessors']);doc['accessors'].append(accessor)
    return index


def matrix(node):
    if 'matrix' in node:return np.array(node['matrix']).reshape(4,4).T
    x,y,z,w=node.get('rotation',[0,0,0,1]);m=np.eye(4)
    m[:3,:3]=[[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],
        [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],
        [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]]
    m[:3,:3]=m[:3,:3]@np.diag(node.get('scale',[1,1,1]));m[:3,3]=node.get('translation',[0,0,0])
    return m


def world_matrices(doc):
    parents={child:i for i,n in enumerate(doc['nodes']) for child in n.get('children',[])}
    cache={}
    def get(i):
        if i not in cache:cache[i]=(get(parents[i]) if i in parents else np.eye(4))@matrix(doc['nodes'][i])
        return cache[i]
    return [get(i) for i in range(len(doc['nodes']))],parents


def assemble(chars_dir,pistol):
    from .pistol_arms import assemble as assemble_authored_arms
    assemble_authored_arms(chars_dir,pistol)
