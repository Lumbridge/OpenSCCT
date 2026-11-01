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
    # Both converted glTF rigs are Y-up. Turn the rifle's -X forward into
    # the pistol's +X forward without swapping vertical bob into lateral sway.
    yaw=.15;c=math.cos(yaw);s=math.sin(yaw)
    align=np.array([[c,0,s],[0,1,0],[-s,0,c]])@np.diag([-1.,1.,-1.])
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
        rigid_motion=clip['name'] in {'FirstWaitStFd2','FirstWalkStFd2','FirstJumpStFd2',
                                     'FirstFallStFd2','FirstLandStFd2','FirstLandStHi2'}
        frames={node:[] for node in animated}
        for time in times:
            ref_pose=pose(reference,source,time);desired={}
            # Rifle and pistol arm proportions differ. Preserve the authored
            # pistol's elbow/wrist shapes and move the complete rig by the
            # primary hand's displacement instead of transplanting each joint.
            motion=align@(ref_pose[by_name['Bip01 R Hand']][:3,3]-ref_idle[by_name['Bip01 R Hand']][:3,3])
            def world(node):
                if node in desired:return desired[node]
                if rigid_motion:
                    value=source_idle[node].copy()
                    if node in animated:value[:3,3]+=motion
                    desired[node]=value;return value
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
        fractions=np.array([0,.10,.18,.26,.28,.36,.42,.48,.58,.60,.68,.72,.76,.78,.88,1.]);times=fractions*duration
        curve(doc,data,animation,66,'scale',times,[[1 if reload and .42<=f<.76 else 0]*3 for f in fractions])
        animation['samplers'][-1]['interpolation']='STEP'
        local=np.linalg.inv(idle[parents[80]])@idle[80]
        axis=np.linalg.inv(idle[parents[80]][:3,:3])@placement[:3,:3]
        cylinders=[];loaders=[]
        for f in fractions:
            opened=min(np.clip((f-.10)/.08,0,1),np.clip((.88-f)/.10,0,1)) if reload else 0
            spin=-opened*.32;c=math.cos(spin);q=math.sin(spin)
            moved=local.copy();moved[:3,3]+=axis@np.array([0,0,-.048*opened])
            moved[:3,:3]=axis@np.array([[1,0,0],[0,c,-q],[0,q,c]])@np.linalg.inv(axis)@local[:3,:3]
            cylinders.append(decompose(moved))
            offset=np.eye(4);offset[0,3]=np.interp(f,[0,.42,.48,.58,.60,.72,1],[-.11,-.11,-.06,0,0,-.12,-.12])
            loaders.append(decompose(moved@np.linalg.inv(idle[80])@placement@offset@np.linalg.inv(placement)@idle[66]))
        for node,poses in [(80,cylinders),(66,loaders)] if reload else [(80,cylinders)]:
            for i,path in enumerate(['translation','rotation']):curve(doc,data,animation,node,path,times,[p[i] for p in poses])
        if not reload:continue
        changed=tracks(doc,data,animation);times=np.linspace(0,duration,72)
        frames={node:[] for node in [7,8,9,10]}
        for time in times:
            old=pose(doc,original,time);new=pose(doc,changed,time);f=time/duration
            blend=min(np.clip((f-.12)/.12,0,1),np.clip((.9-f)/.12,0,1))
            # Solve the hand against the rigid loader pose, independent of its visibility scale.
            offset=np.eye(4);offset[0,3]=np.interp(f,[0,.42,.48,.58,.60,.72,1],[-.11,-.11,-.06,0,0,-.12,-.12])
            loader=new[80]@np.linalg.inv(idle[80])@placement@offset@np.linalg.inv(placement)@idle[66]
            follow=loader@np.linalg.inv(idle[66])
            target=(follow@idle[10])[:3,3]
            target+=(new[58]@np.linalg.inv(idle[58]))[:3,:3]@placement[:3,:3]@np.array([-.01,-.02,.015])
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


def revolver_cartridges(doc,data,idle,placement,mesh_world):
    """Animate the source model's textured cartridges, with hollow spent cases cut from the same mesh."""
    from .reload_motion import smooth
    rest,parents=world_matrices(doc);skin=doc['skins'][0]
    binds=values(doc,data,skin['inverseBindMatrices']).tolist()
    fresh=[next(i for i,n in enumerate(doc['nodes']) if n.get('name')==f'FreshRound{k}') for k in range(6)]
    spent=[];centres=[]
    gun=next(n for n in doc['nodes'] if n.get('name')=='magnum44_parts')
    to_neutral=np.linalg.inv(placement)@idle[80]@np.linalg.inv(rest[80])@mesh_world
    for i,node in enumerate(fresh):
        joint=skin['joints'].index(node)
        case=len(doc['nodes']);doc['nodes'].append({'name':f'SpentCase{i}','translation':[0,0,0]})
        doc['nodes'][80].setdefault('children',[]).append(case);spent.append(case)
        case_joint=len(skin['joints']);skin['joints'].append(case)
        binds.append((np.linalg.inv(rest[80])@mesh_world).T.reshape(-1).tolist())
        primitives=[];points=[]
        for part in doc['meshes'][gun['mesh']]['primitives']:
            joints=values(doc,data,part['attributes']['JOINTS_0'])[:,0]
            if not np.any(joints==joint):continue
            xyz=values(doc,data,part['attributes']['POSITION'])
            neutral=(to_neutral[:3,:3]@xyz.T).T+to_neutral[:3,3]
            points.extend(neutral[joints==joint])
            triangles=values(doc,data,part['indices']).reshape(-1,3)
            # The source case mouth is at X=.1191. Discard the projectile and keep
            # the rim, stamped case head, primer, UVs and brass body. The mouth is open.
            keep=np.all((joints[triangles]==joint)&(neutral[triangles,0]<=.11912),axis=1)
            triangles=triangles[keep]
            used,inverse=np.unique(triangles,return_inverse=True);attrs={}
            for key,index in part['attributes'].items():
                accessor=doc['accessors'][index];array=values(doc,data,index)[used]
                if key=='JOINTS_0':array[:]=[case_joint,0,0,0]
                attrs[key]=append(doc,data,array,accessor['type'],accessor['componentType'],34962)
            primitives.append({'attributes':attrs,'indices':append(doc,data,inverse.reshape(-1,1),'SCALAR',5125,34963),'material':part['material']})
        points=np.asarray(points);centres.append((points.min(0)+points.max(0))/2)
        doc['meshes'].append({'name':f'SpentCase{i}','primitives':primitives})
        doc['nodes'].append({'name':f'SpentCase{i}_mesh','mesh':len(doc['meshes'])-1,'skin':0})
        doc['nodes'][5].setdefault('children',[]).append(len(doc['nodes'])-1)
    skin['inverseBindMatrices']=append(doc,data,binds,'MAT4')
    for animation in doc['animations']:
        compiled=tracks(doc,data,animation)
        duration=max(float(t[-1]) for _,_,t,_ in compiled);reload=animation['name']=='FirstReloStFd2'
        fractions=np.unique(np.r_[np.linspace(0,1,121),.28,.42,.44,.48,.58,.60,.72,.76]) if reload else np.array([0.,1.])
        emitted=pose(doc,compiled,.28*duration)[80]@np.linalg.inv(idle[80])@placement
        seated=pose(doc,compiled,.58*duration) if reload else None
        loader_to_cylinder=np.linalg.inv(seated[66])@seated[80] if reload else None
        frames={node:[] for node in fresh+spent}
        for f in fractions:
            current=pose(doc,compiled,f*duration)
            cylinder=current[80]@np.linalg.inv(idle[80])@placement
            for i,(live,case) in enumerate(zip(fresh,spent)):
                # Fresh rounds travel rigidly with the speedloader until seated;
                # an independent axial curve drifts through the hand on approach.
                desired=current[66]@loader_to_cylinder if reload and .38<=f<.58 else current[80]
                frames[live].append(decompose(np.linalg.inv(current[80])@desired))
                flight=max(0,(f-.28)*duration) if reload else 0
                offset=np.eye(4)
                if flight>0:
                    # Clear the chamber before tumbling. Ejection follows the
                    # tilted bore, but gravity stays down in model/world space.
                    angle=max(0,flight-.07)*(3+i*.35);c=math.cos(angle);q=math.sin(angle)
                    offset[:3,:3]=[[c,-q,0],[q,c,0],[0,0,1]]
                    offset[:3,3]=centres[i]-offset[:3,:3]@centres[i]
                desired=(emitted if flight>0 else cylinder)@offset@np.linalg.inv(placement)@idle[80]
                if reload:
                    mechanism = emitted if flight>0 else cylinder
                    rear = -mechanism[:3,0]/np.linalg.norm(mechanism[:3,0])
                    # The hand first pushes the extractor and lifts the cases
                    # out of their chambers; free fall begins at release.
                    desired[:3,3] += rear*smooth([0,.24,.28,1],np.array([0,0,.018,.018]),f)
                if flight>0:
                    rear=-emitted[:3,0]/np.linalg.norm(emitted[:3,0])
                    lateral=emitted[:3,2]/np.linalg.norm(emitted[:3,2])
                    desired[:3,3]+=rear*.20*flight + lateral*(i-2.5)*.012*flight + np.array([0,-4.905*flight*flight,0])
                frames[case].append(decompose(np.linalg.inv(current[80])@desired))
        # The full reload bake already contains constant cartridge channels.
        # Replace them with the extraction/loading tracks, never duplicate a
        # glTF node/property target (importers disagree about which one wins).
        cartridge_nodes=set(fresh+spent)
        animation['channels']=[c for c in animation['channels'] if c['target']['node'] not in cartridge_nodes]
        for node,poses in frames.items():
            for i,path in enumerate(['translation','rotation']):curve(doc,data,animation,node,path,fractions*duration,[p[i] for p in poses])
            visible=[(not reload or f>=.42) if node in fresh else (reload and f<.44) for f in fractions]
            curve(doc,data,animation,node,'scale',fractions*duration,[[1 if v else 0]*3 for v in visible])
            animation['samplers'][-1]['interpolation']='STEP'


def pistol_melee(doc,data):
    """A compact pistol-whip with both wrists and the gun moving together, rather than a stretched rifle-butt pose."""
    idle_clip=next(a for a in doc['animations'] if a['name']=='FirstWaitStFd2')
    held=tracks(doc,data,idle_clip);neutral=pose(doc,held,.5);_,parents=world_matrices(doc)
    fractions=np.array([0,.16,.30,.39,.56,.78,1.])
    moves=np.array([[0,0,0],[.055,-.025,.035],[-.115,.035,-.13],[-.13,.025,-.10],[-.055,-.02,.025],[.008,-.01,.01],[0,0,0]])
    angles=[0,-.18,.90,.65,.15,-.025,0]
    for animation in doc['animations']:
        if animation['name'] not in ['FirstAtakCrFd2','FirstButtStFd2','FirstAtakChit2']:continue
        duration=max(float(t[-1]) for _,_,t,_ in tracks(doc,data,animation))
        animation['channels']=[];animation['samplers']=[]
        for node,path,times,frames in held:
            curve(doc,data,animation,node,path,[0,duration],[sample(times,frames,.5,path=='rotation')]*2)
        roots=[6,57]
        animation['channels']=[c for c in animation['channels'] if c['target']['node'] not in roots]
        for node in roots:
            poses=[]
            for move,angle in zip(moves,angles):
                c=math.cos(angle);s=math.sin(angle);delta=np.eye(4)
                delta[:3,:3]=[[c,0,s],[0,1,0],[-s,0,c]]
                pivot=neutral[57][:3,3]
                delta[:3,3]=move+pivot-delta[:3,:3]@pivot
                poses.append(decompose(np.linalg.inv(neutral[parents[node]])@delta@neutral[node]))
            for i,path in enumerate(['translation','rotation','scale']):curve(doc,data,animation,node,path,fractions*duration,[p[i] for p in poses])


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


def mount_to_firing_hand(doc,data):
    """Keep the rigid weapon attached even when independent action/movement clips blend."""
    names={node.get('name'):i for i,node in enumerate(doc['nodes'])}
    gun=names['pistol']
    wrist=next(i for name,i in names.items() if name and name.startswith('R_wrist_'))
    rest,parents=world_matrices(doc)
    idle=next(a for a in doc['animations'] if a['name']=='FirstWaitStFd2')
    held=pose(doc,tracks(doc,data,idle),0)
    grip=np.linalg.inv(held[wrist])@held[gun]
    # Preserve the bind-space world transforms and inverse binds; only the
    # hierarchy changes. All mechanisms remain children of the mounted gun.
    doc['nodes'][parents[gun]]['children'].remove(gun)
    doc['nodes'][wrist].setdefault('children',[]).append(gun)
    node=doc['nodes'][gun]
    node.pop('matrix',None)
    for key,value in zip(('translation','rotation','scale'),decompose(np.linalg.inv(rest[wrist])@rest[gun])):
        node[key]=value.tolist()
    for animation in doc['animations']:
        duration=max(float(t[-1]) for _,_,t,_ in tracks(doc,data,animation))
        animation['channels']=[c for c in animation['channels'] if c['target']['node']!=gun]
        for key,value in zip(('translation','rotation','scale'),decompose(grip)):
            curve(doc,data,animation,gun,key,[0,duration],[value,value])


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
    if pistol=='magnum44':
        for i in range(6):
            node=len(doc['nodes']);doc['nodes'].append({'name':f'FreshRound{i}','translation':[0,0,0]})
            doc['nodes'][80].setdefault('children',[]).append(node)
            skin_nodes['round'+str(i)]=len(skin['joints']);skin['joints'].append(node)
            inverse_bind.append((np.linalg.inv(rest_world[80])@mesh_world).T.reshape(-1).tolist())
        rest_world,_=world_matrices(doc)
        idle_world=pose(doc,source,8.8)
    skin['inverseBindMatrices']=append(doc,data,inverse_bind,'MAT4')
    # Neutral authored pistol frame -> source grip, then through the camera framing root.
    placement=camera@np.array([[0.,0,-1,0],[0,1,0,.089],[1,0,0,.153],[0,0,0,1]])
    append_parts(doc,data,folder/(pistol+'.glb'),placement,skin_nodes,rest_world,idle_world,mesh_world)
    if pistol=='magnum44':append_parts(doc,data,folder/'magnum44_loader.glb',placement,skin_nodes,rest_world,idle_world,mesh_world)
    # FX joints belong to the new barrel, never to the removed source pistol.
    for name,position in [('FX',[{'suppressed':.36,'unsuppressed':.203,'automatic':.176,'magnum44':.338,'desert_eagle':.24}[pistol],.023,0]),('FX_DOUILLE',[.08,.03,.02])]:
        # Preserve the authored barrel frame as well as its position. Inheriting the grip bone's
        # rotation points FX sideways; the renderer traces the laser along this joint's local +X.
        world=placement.copy();world[:3,3]=(placement@np.r_[position,1])[:3]
        local=np.linalg.inv(idle_world[58])@world
        translation,orientation,scale=decompose(local)
        doc['nodes'].append({'name':name,'translation':translation.tolist(),
                             'rotation':orientation.tolist(),'scale':scale.tolist()});node=len(doc['nodes'])-1
        doc['nodes'][58].setdefault('children',[]).append(node);skin['joints'].append(node)
    inverse_bind=values(doc,data,skin['inverseBindMatrices']).tolist()
    current_world,_=world_matrices(doc)
    for node in skin['joints'][-2:]:inverse_bind.append((np.linalg.inv(current_world[node])@mesh_world).T.reshape(-1).tolist())
    skin['inverseBindMatrices']=append(doc,data,inverse_bind,'MAT4')
    aliases={'FirstShooStFd2':(0,11),'FirstReloStFd2':(12,82),
        'FirstDrawStEd2':(176,187),'FirstDrawStBg2':(187,233)}
    doc['animations']=[slice_clip(doc,data,source,name,*range_) for name,range_ in aliases.items()]
    idle_world=pose(doc,source,8.8);_,parents=world_matrices(doc)
    reference,reference_data=read(chars_dir/'SWeaponFirst/1DEF_01.glb')
    # Use the primary's idle and speed-driven walk motion on the sidearm rig too.
    retarget_context(doc,data,reference,reference_data,idle_world,parents)
    if pistol=='magnum44':
        revolver_reload(doc,data,idle_world,parents,placement)
    fit_grip(doc,data,pistol,parents)
    pistol_melee(doc,data)
    doc['extras']={'svmap':{'mesh':'OpenScctSidearms.'+pistol+'_first'},'license':'CC-BY-4.0; see CREDITS.txt'}
    from .reload_motion import author
    author(doc,data,pistol)
    if pistol=='magnum44':
        revolver_cartridges(doc,data,idle_world,placement,mesh_world)
    mount_to_firing_hand(doc,data)
    write(folder/(pistol+'_first.glb'),doc,data)
