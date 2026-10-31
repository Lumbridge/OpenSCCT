"""Assemble credited, authored pistol hands; retarget the merc's contextual clips onto the new rig."""
from copy import deepcopy
import math
import numpy as np

from .sidearm_models import read, write, values, append, matrix, world_matrices


def rotation(m):
    return m[:3,:3]/np.maximum(1e-12,np.linalg.norm(m[:3,:3],axis=0))


def quaternion(m):
    # Unit quaternion from a proper rotation, including the 180-degree cases.
    q=np.array([1+m[0,0]-m[1,1]-m[2,2],1-m[0,0]+m[1,1]-m[2,2],
                1-m[0,0]-m[1,1]+m[2,2],1+np.trace(m)])
    i=int(q.argmax());v=math.sqrt(max(0,q[i]))*.5
    if i==3:q=np.array([m[2,1]-m[1,2],m[0,2]-m[2,0],m[1,0]-m[0,1],4*v*v])/(4*v)
    else:
        j=(i+1)%3;k=(i+2)%3;result=np.zeros(4);result[i]=v
        result[j]=(m[j,i]+m[i,j])/(4*v);result[k]=(m[k,i]+m[i,k])/(4*v)
        result[3]=(m[k,j]-m[j,k])/(4*v);q=result
    return q/np.linalg.norm(q)


def decompose(m):
    scale=np.linalg.norm(m[:3,:3],axis=0)
    return m[:3,3],quaternion(m[:3,:3]/np.maximum(1e-12,scale)),scale


def tracks(doc,data,animation):
    result=[]
    for channel in animation.get('channels',[]):
        sampler=animation['samplers'][channel['sampler']]
        result.append((channel['target']['node'],channel['target']['path'],
            values(doc,data,sampler['input'])[:,0],values(doc,data,sampler['output'])))
    return result


def sample(times,frames,time,quaternions=False):
    hi=int(np.searchsorted(times,time,side='right'))
    if hi==0:return frames[0].copy()
    if hi==len(times):return frames[-1].copy()
    lo=hi-1;a=frames[lo];b=frames[hi]
    if quaternions and a@b<0:b=-b
    value=a+(b-a)*((time-times[lo])/(times[hi]-times[lo]))
    return value/np.linalg.norm(value) if quaternions else value


def pose(doc,compiled,time):
    nodes=[dict(node) for node in doc['nodes']]
    for node,path,times,frames in compiled:
        nodes[node].pop('matrix',None)
        nodes[node][path]=sample(times,frames,time,path=='rotation')
    return world_matrices({'nodes':nodes})[0]


def curve(doc,data,animation,node,path,times,frames):
    kind='VEC4' if path=='rotation' else 'VEC3'
    t=append(doc,data,np.asarray(times).reshape(-1,1),'SCALAR')
    v=append(doc,data,np.asarray(frames),kind)
    sampler=len(animation['samplers']);animation['samplers'].append({'input':t,'output':v,'interpolation':'LINEAR'})
    animation['channels'].append({'sampler':sampler,'target':{'node':node,'path':path}})


def slice_clip(doc,data,source,name,first,last):
    start=first/30;end=last/30
    times=np.linspace(start,end,max(2,last-first+1))
    animation={'name':name,'samplers':[],'channels':[]}
    for node,path,key_times,frames in source:
        curve(doc,data,animation,node,path,times-start,
            [sample(key_times,frames,t,path=='rotation') for t in times])
    return animation


def append_parts(doc,data,path,placement,skin_nodes,rest_world,idle_world,mesh_world):
    own,blob=read(path);own_world,_=world_matrices(own)
    image_offset=len(doc['images']);texture_offset=len(doc['textures']);material_offset=len(doc['materials'])
    doc['images'].extend(deepcopy(own['images']))
    for texture in deepcopy(own['textures']):
        if 'source' in texture:texture['source']+=image_offset
        texture.pop('sampler',None);doc['textures'].append(texture)
    def remap(value):
        if not isinstance(value,dict):return
        for key,child in value.items():
            if key.endswith('Texture') and isinstance(child,dict) and 'index' in child:child['index']+=texture_offset
            else:remap(child)
    for material in deepcopy(own['materials']):remap(material);doc['materials'].append(material)
    inverse_mesh=np.linalg.inv(mesh_world)
    for i,node in enumerate(own['nodes']):
        if 'mesh' not in node:continue
        own_joints=own['skins'][node['skin']]['joints']
        mapping=[skin_nodes.get(own['nodes'][j]['name'],skin_nodes['corps']) for j in own_joints]
        primitives=[]
        for part in own['meshes'][node['mesh']]['primitives']:
            attributes={};joints=values(own,blob,part['attributes']['JOINTS_0'])
            weights=values(own,blob,part['attributes']['WEIGHTS_0'])
            dominant=joints[np.arange(len(joints)),weights.argmax(axis=1)]
            transforms=[inverse_mesh@rest_world[doc['skins'][0]['joints'][j]]@
                np.linalg.inv(idle_world[doc['skins'][0]['joints'][j]])@placement@own_world[i] for j in mapping]
            for key,index in part['attributes'].items():
                accessor=own['accessors'][index];array=values(own,blob,index)
                if key in ['POSITION','NORMAL']:
                    output=array.copy()
                    for joint in np.unique(dominant):
                        selected=dominant==joint;transform=transforms[joint]
                        if key=='POSITION':output[selected]=(transform[:3,:3]@array[selected].T).T+transform[:3,3]
                        else:
                            output[selected]=(np.linalg.inv(transform[:3,:3]).T@array[selected].T).T
                            output[selected]/=np.maximum(1e-8,np.linalg.norm(output[selected],axis=1,keepdims=True))
                    array=output
                elif key=='JOINTS_0':array=np.asarray(mapping,dtype=np.uint16)[array]
                elif key=='TANGENT':continue  # glTF loader generates tangents against the transformed normals and UVs.
                attributes[key]=append(doc,data,array,accessor['type'],5123 if key=='JOINTS_0' else accessor['componentType'],34962)
            index_data=values(own,blob,part['indices'])
            primitives.append({'attributes':attributes,'indices':append(doc,data,index_data,'SCALAR',5125,34963),
                'material':material_offset+part.get('material',0)})
        mesh=len(doc['meshes']);doc['meshes'].append({'name':path.stem,'primitives':primitives})
        doc['nodes'].append({'name':path.stem+'_parts','mesh':mesh,'skin':0})
        doc['nodes'][5].setdefault('children',[]).append(len(doc['nodes'])-1)


def retarget_context(doc,data,reference,reference_data,source_idle,source_parent):
    by_name={node.get('name'):i for i,node in enumerate(reference['nodes'])}
    ref_idle_clip=next(a for a in reference['animations'] if a['name']=='FirstWaitStFd2')
    ref_idle=pose(reference,tracks(reference,reference_data,ref_idle_clip),0)
    # Converted rifle frame -> the new camera frame: -X forward, +Y right, +Z up in the reference.
    yaw=.15;c=math.cos(yaw);s=math.sin(yaw)
    align=np.array([[c,0,s],[0,1,0],[-s,0,c]])@np.array([[-1.,0,0],[0,0,1],[0,1,0]])
    mapping={}
    for i,node in enumerate(doc['nodes']):
        name=node.get('name','')
        for side in ['L','R']:
            for prefix,reference_name in [('arm','UpperArm'),('elbow','Forearm'),('wrist','Hand')]:
                if name.startswith(side+'_'+prefix+'_'):mapping[i]=by_name.get('Bip01 '+side+' '+reference_name)
            for finger,index in [('thumb',0),('point',1),('middle',2),('ring',3),('pink',4)]:
                for segment in range(1,5):
                    if name.startswith(f'{side}_{finger}{segment}_'):
                        suffix='' if segment==1 else str(segment-1) if segment<4 else '2'
                        mapping[i]=by_name.get(f'Bip01 {side} Finger{index}{suffix}')
    mapping={node:ref for node,ref in mapping.items() if ref is not None}
    wrist=next(i for i,n in enumerate(doc['nodes']) if n.get('name','').startswith('R_wrist_'))
    idle_local=[np.linalg.inv(source_idle[source_parent[i]])@m if i in source_parent else m for i,m in enumerate(source_idle)]
    for clip in reference['animations']:
        if clip['name'] in {a['name'] for a in doc['animations']}:continue
        source=tracks(reference,reference_data,clip)
        duration=max(float(t[-1]) for _,_,t,_ in source)
        times=np.linspace(0,duration,max(2,math.ceil(duration*30)+1))
        animated=set(mapping)|set(doc['skins'][0]['joints'])|{57}
        frames={node:[] for node in animated}
        for time in times:
            ref_pose=pose(reference,source,time);desired={}
            def world(node):
                if node in desired:return desired[node]
                parent=source_parent.get(node)
                value=(world(parent)@idle_local[node]) if parent is not None else idle_local[node].copy()
                if node in mapping:
                    old=mapping[node];value=source_idle[node].copy()
                    delta=align@rotation(ref_pose[old])@rotation(ref_idle[old]).T@align.T
                    value[:3,:3]=delta@source_idle[node][:3,:3]
                    value[:3,3]+=align@(ref_pose[old][:3,3]-ref_idle[old][:3,3])
                if node==57:value=world(wrist)@np.linalg.inv(source_idle[wrist])@source_idle[node]
                desired[node]=value;return value
            for node in animated:
                m=world(node);parent=source_parent.get(node)
                frames[node].append(decompose(np.linalg.inv(world(parent))@m if parent is not None else m))
        animation={'name':clip['name'],'samplers':[],'channels':[]}
        for node,poses in frames.items():
            for index,path in enumerate(['translation','rotation','scale']):
                curve(doc,data,animation,node,path,times,[p[index] for p in poses])
        doc['animations'].append(animation)


def turn(from_,to):
    a=from_/max(1e-12,np.linalg.norm(from_));b=to/max(1e-12,np.linalg.norm(to))
    axis=np.cross(a,b);cosine=float(a@b)
    skew=np.array([[0,-axis[2],axis[1]],[axis[2],0,-axis[0]],[-axis[1],axis[0],0]])
    return np.eye(3)+skew+skew@skew/max(1e-8,1+cosine)


def revolver_reload(doc,data,idle,parents,placement):
    """Open the cylinder, show a six-cartridge loader, and bring the supporting hand to the loader."""
    for animation in doc['animations']:
        original=tracks(doc,data,animation)
        duration=max(float(t[-1]) for _,_,t,_ in original)
        reload=animation['name']=='FirstReloStFd2'
        animation['channels']=[c for c in animation['channels'] if not
            (c['target']['node']==66 and c['target']['path']=='scale') and not
            (c['target']['node']==80 and c['target']['path'] in ['translation','rotation']) and not
            (reload and c['target']['node']==66 and c['target']['path']=='translation')]
        fractions=np.array([0,.12,.22,.35,.55,.75,.88,1.]);times=fractions*duration
        curve(doc,data,animation,66,'scale',times,[[1 if reload and .22<=f<=.75 else 0]*3 for f in fractions])
        for node in [80,66] if reload else [80]:
            parent=parents[node];local=np.linalg.inv(idle[parent])@idle[node]
            position,orientation,_=decompose(local);positions=[]
            for f in fractions:
                delta=np.zeros(3)
                if reload and .12<=f<=.88:
                    delta[2]=-.045
                    if node==66:delta[0]=-.035 if f<.55 else .005
                positions.append(position+np.linalg.inv(idle[parent][:3,:3])@delta)
            curve(doc,data,animation,node,'translation',times,positions)
            if node==80:curve(doc,data,animation,node,'rotation',times,[orientation]*len(times))
        if not reload:continue
        changed=tracks(doc,data,animation);times=np.linspace(0,duration,72)
        frames={node:[] for node in [7,8,9,10]}
        for time in times:
            old=pose(doc,original,time);new=pose(doc,changed,time);f=time/duration
            blend=min(np.clip((f-.12)/.12,0,1),np.clip((.9-f)/.12,0,1))
            target=(new[66]@np.linalg.inv(old[66])@old[10])[:3,3]
            target+=(new[58]@np.linalg.inv(idle[58]))[:3,:3]@placement[:3,:3]@np.array([.04,.067,0])
            target=old[10][:3,3]*(1-blend)+target*blend
            shoulder=old[7][:3,3];elbow=old[8][:3,3];wrist=old[10][:3,3]
            a=np.linalg.norm(elbow-shoulder);b=np.linalg.norm(wrist-elbow);direction=target-shoulder
            length=np.clip(np.linalg.norm(direction),abs(a-b)+1e-5,a+b-1e-5);direction/=max(1e-8,np.linalg.norm(direction))
            target=shoulder+direction*length;pole=elbow-shoulder;pole-=direction*(pole@direction);pole/=max(1e-8,np.linalg.norm(pole))
            along=(a*a-b*b+length*length)/(2*length)
            new_elbow=shoulder+direction*along+pole*math.sqrt(max(0,a*a-along*along))
            upper=turn(elbow-shoulder,new_elbow-shoulder);lower=turn(wrist-elbow,target-new_elbow)
            desired={node:old[node].copy() for node in frames}
            desired[7][:3,:3]=upper@old[7][:3,:3]
            desired[8][:3,:3]=lower@old[8][:3,:3];desired[8][:3,3]=new_elbow
            desired[9][:3,:3]=lower@old[9][:3,:3];desired[9][:3,3]=new_elbow+lower@(old[9][:3,3]-elbow)
            desired[10][:3,3]=target
            for node in frames:
                parent=parents[node];parent_world=desired.get(parent,old[parent])
                frames[node].append(decompose(np.linalg.inv(parent_world)@desired[node]))
        animation['channels']=[c for c in animation['channels'] if c['target']['node'] not in frames]
        for node,poses in frames.items():
            for i,path in enumerate(['translation','rotation','scale']):curve(doc,data,animation,node,path,times,[p[i] for p in poses])


def fit_grip(doc,data,pistol,parents):
    """Fit gripping fingers to each handle, preserving the authored trigger finger and thumb."""
    skin=doc['skins'][0]
    binds=values(doc,data,skin['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
    geometry=[]
    for node in doc['nodes']:
        if node.get('name')!=pistol+'_parts':continue
        for part in doc['meshes'][node['mesh']]['primitives']:
            xyz=values(doc,data,part['attributes']['POSITION'])
            joints=values(doc,data,part['attributes']['JOINTS_0'])[:,0]
            for joint in np.unique(joints):geometry.append((int(joint),xyz[joints==joint]))
    idle_animation=next(a for a in doc['animations'] if a['name']=='FirstWaitStFd2')
    idle=pose(doc,tracks(doc,data,idle_animation),.5);surface=[]
    for joint,xyz in geometry:
        m=idle[skin['joints'][joint]]@binds[joint];surface.extend((m[:3,:3]@xyz.T).T+m[:3,3])
    surface=np.asarray(surface)
    wrist=next(i for i,n in enumerate(doc['nodes']) if n.get('name','').startswith('R_wrist_'))
    fingers=[]
    for finger in ['middle','ring','pink']:
        nodes=[next(i for i,n in enumerate(doc['nodes']) if n.get('name','').startswith(f'R_{finger}{segment}_')) for segment in [1,2,3]]
        tip=idle[nodes[2]][:3,3];nearest=surface[np.linalg.norm(surface-tip,axis=1).argmin()]
        direction=tip-nearest;distance=np.linalg.norm(direction)
        if .005<distance<.03:
            target=nearest+direction/distance*.005
            fingers.append((nodes,np.linalg.inv(idle[wrist])@np.r_[target,1]))
    if not fingers:return
    for animation in doc['animations']:
        if animation['name'] not in ['FirstWaitStFd2','FirstWalkStFd2','FirstShooStFd2','FirstReloStFd2']:continue
        original=tracks(doc,data,animation);duration=max(float(t[-1]) for _,_,t,_ in original)
        times=np.linspace(0,duration,max(2,math.ceil(duration*30)+1));frames={node:[] for nodes,_ in fingers for node in nodes}
        for time in times:
            old=pose(doc,original,time);desired={}
            for (root,joint,tip),local_target in fingers:
                target=(old[wrist]@local_target)[:3]
                start=old[root][:3,3];middle=old[joint][:3,3];end=old[tip][:3,3]
                a=np.linalg.norm(middle-start);b=np.linalg.norm(end-middle);direction=target-start
                length=np.clip(np.linalg.norm(direction),abs(a-b)+1e-6,a+b-1e-6);direction/=max(1e-9,np.linalg.norm(direction))
                target=start+direction*length;pole=middle-start;pole-=direction*(pole@direction);pole/=max(1e-9,np.linalg.norm(pole))
                along=(a*a-b*b+length*length)/(2*length)
                new_middle=start+direction*along+pole*math.sqrt(max(0,a*a-along*along))
                upper=turn(middle-start,new_middle-start);lower=turn(end-middle,target-new_middle)
                for node in [root,joint,tip]:desired[node]=old[node].copy()
                desired[root][:3,:3]=upper@old[root][:3,:3]
                desired[joint][:3,:3]=lower@old[joint][:3,:3];desired[joint][:3,3]=new_middle
                desired[tip][:3,:3]=lower@old[tip][:3,:3];desired[tip][:3,3]=target
            for node in frames:
                parent=parents[node];parent_world=desired.get(parent,old[parent])
                frames[node].append(decompose(np.linalg.inv(parent_world)@desired[node]))
        animation['channels']=[c for c in animation['channels'] if c['target']['node'] not in frames]
        for node,poses in frames.items():
            for i,path in enumerate(['translation','rotation','scale']):curve(doc,data,animation,node,path,times,[p[i] for p in poses])


def assemble(chars_dir,pistol):
    folder=chars_dir/'OpenScctSidearms';doc,data=read(folder/'arms_base.glb')
    source=tracks(doc,data,doc['animations'][0])
    # The authored source is Z-forward. Frame it lower-right without changing the finger and wrist relationship.
    yaw=.15;c=math.cos(yaw);s=math.sin(yaw)
    camera=np.array([[-s,0,c,.36-.171*c],[0,1,0,-.205],[-c,0,-s,.16+.171*s],[0,0,0,1]])
    roots=doc['scenes'][doc.get('scene',0)]['nodes'];frame=len(doc['nodes'])
    doc['nodes'].append({'name':'MercPistolFrame','matrix':camera.T.reshape(-1).tolist(),'children':roots})
    doc['scenes'][doc.get('scene',0)]['nodes']=[frame]
    rest_world,parents=world_matrices(doc);idle_world=pose(doc,source,8.8)
    skin=doc['skins'][0];mesh_world=rest_world[83]
    inverse_bind=values(doc,data,skin['inverseBindMatrices']).tolist()
    skin_nodes={}
    for name,node in [('corps',58),('pompe',80),('chargeur',66)]:
        skin_nodes[name]=len(skin['joints']);skin['joints'].append(node)
        inverse_bind.append((np.linalg.inv(rest_world[node])@mesh_world).T.reshape(-1).tolist())
    skin['inverseBindMatrices']=append(doc,data,inverse_bind,'MAT4')
    # Neutral authored pistol frame -> source grip, then through the camera framing root.
    placement=camera@np.array([[0.,0,-1,0],[0,1,0,.089],[1,0,0,.153],[0,0,0,1]])
    append_parts(doc,data,folder/(pistol+'.glb'),placement,skin_nodes,rest_world,idle_world,mesh_world)
    if pistol=='magnum44':append_parts(doc,data,folder/'magnum44_loader.glb',placement,skin_nodes,rest_world,idle_world,mesh_world)
    # FX joints belong to the new barrel, never to the removed source pistol.
    for name,position in [('FX',[{'suppressed':.36,'automatic':.176,'magnum44':.338,'desert_eagle':.24}[pistol],.023,0]),('FX_DOUILLE',[.08,.03,.02])]:
        world=placement@np.r_[position,1];local=np.linalg.inv(idle_world[58])@world
        doc['nodes'].append({'name':name,'translation':local[:3].tolist()});node=len(doc['nodes'])-1
        doc['nodes'][58].setdefault('children',[]).append(node);skin['joints'].append(node)
    inverse_bind=values(doc,data,skin['inverseBindMatrices']).tolist()
    current_world,_=world_matrices(doc)
    for node in skin['joints'][-2:]:inverse_bind.append((np.linalg.inv(current_world[node])@mesh_world).T.reshape(-1).tolist())
    skin['inverseBindMatrices']=append(doc,data,inverse_bind,'MAT4')
    aliases={'FirstShooStFd2':(0,11),'FirstReloStFd2':(12,82),'FirstWaitStFd2':(234,264),
        'FirstWalkStFd2':(234,264),'FirstDrawStEd2':(176,187),'FirstDrawStBg2':(187,233)}
    doc['animations']=[slice_clip(doc,data,source,name,*range_) for name,range_ in aliases.items()]
    idle_world=pose(doc,source,8.8);_,parents=world_matrices(doc)
    reference,reference_data=read(chars_dir/'SWeaponFirst/1DEF_01.glb')
    retarget_context(doc,data,reference,reference_data,idle_world,parents)
    if pistol=='magnum44':revolver_reload(doc,data,idle_world,parents,placement)
    fit_grip(doc,data,pistol,parents)
    doc['extras']={'svmap':{'mesh':'OpenScctSidearms.'+pistol+'_first'},'license':'CC-BY-4.0; see CREDITS.txt'}
    write(folder/(pistol+'_first.glb'),doc,data)
