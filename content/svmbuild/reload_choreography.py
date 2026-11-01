"""Contact-driven reload timelines built from calibrated grip poses.

Only reference hand shapes and grip offsets are sampled from the source reload.
Weapon, hands and removable parts follow independently authored trajectories.
"""
import math
from copy import deepcopy
import numpy as np

from .sidearm_models import world_matrices, matrix, values, append
from .pistol_arms import tracks, pose, curve, decompose, sample
from .reload_motion import solve_arm, smooth

MAIN_CLIPS = {'rifle': 'FirstReloStFd2', 'uzi': 'FirstReloStFd2U', 'shotgun': 'FirstReloStFd2S'}
SHELL_TIMES = (.7880001, 1.11, 1.4413334, 1.9193333, 2.2793334)
STYLE = {  # roll, pitch, yaw, draw towards camera, magazine travel
    'rifle': (-48, 8, 24, -.12, .24), 'uzi': (-52, 10, 20, -.11, .08),
    'shotgun': (-54, 8, 16, -.12, .17),
    # Keep pistols upright: lift the muzzle slightly to expose the magazine
    # well instead of laying the whole weapon and firing wrist on their side.
    'suppressed': (-8, -20, 8, -.04, .22),
    'unsuppressed': (-10, -20, 9, -.045, .23), 'automatic': (-12, -22, 10, -.05, .21),
    'desert_eagle': (-8, -19, 8, -.065, .24), 'magnum44': (-16, -18, 12, -.05, .18),
}


def rotation(axis, angle):
    axis = np.asarray(axis, dtype=float); axis /= np.linalg.norm(axis)
    x, y, z = axis
    k = np.array([[0, -z, y], [z, 0, -x], [-y, x, 0]])
    return np.eye(3) + math.sin(angle)*k + (1-math.cos(angle))*(k@k)


def mix(a, b, t):
    """Interpolate transforms, with shortest-path rotation and eased transitions."""
    t = float(np.clip(t, 0, 1)); t = t*t*(3-2*t)
    pa, qa, sa = decompose(a); pb, qb, sb = decompose(b)
    q = sample(np.array([0., 1.]), np.array([qa, qb]), t, True)
    return matrix({'translation': pa+(pb-pa)*t, 'rotation': q, 'scale': sa+(sb-sa)*t})


def path(keys, poses, t):
    i = int(np.clip(np.searchsorted(keys, t, side='right')-1, 0, len(keys)-2))
    return mix(poses[i], poses[i+1], (t-keys[i])/(keys[i+1]-keys[i]))


def shift(pose_, offset):
    result = pose_.copy(); result[:3, 3] += offset
    return result


def add_shell(doc, data):
    """A visible, rigged shotgun shell; original gun/arm geometry stays intact."""
    skin=doc['skins'][0]; joint=len(skin['joints']); node=len(doc['nodes'])
    doc['nodes'].append({'name':'ReloadShell'})
    doc['nodes'][skin['joints'][0]].setdefault('children',[]).append(node)
    skin['joints'].append(node)
    binds=values(doc,data,skin['inverseBindMatrices']).tolist()
    binds.append(np.eye(4).T.reshape(-1).tolist())
    skin['inverseBindMatrices']=append(doc,data,binds,'MAT4')
    parts=[]
    for z0,z1,radius,color in [(0,.009,.010,[.55,.32,.08,1]),(.009,.047,.009,[.46,.025,.018,1])]:
        verts=[];normals=[];indices=[]
        for i in range(17):
            a=i*math.tau/16; x,y=radius*math.cos(a),radius*math.sin(a)
            verts.extend([[x,y,z0],[x,y,z1]]);normals.extend([[math.cos(a),math.sin(a),0]]*2)
            if i<16:indices.extend([2*i,2*i+2,2*i+1,2*i+1,2*i+2,2*i+3])
        for z,normal in [(z0,-1),(z1,1)]:
            center=len(verts);verts.append([0,0,z]);normals.append([0,0,normal])
            for i in range(17):
                a=i*math.tau/16;verts.append([radius*math.cos(a),radius*math.sin(a),z]);normals.append([0,0,normal])
                if i<16:indices.extend([center,center+i+1,center+i+2] if normal>0 else [center,center+i+2,center+i+1])
        # The game's material compiler consumes external textures, not glTF's
        # baseColorFactor. Tiny solid textures keep these colours in-game too.
        image=len(doc.setdefault('images',[]));doc['images'].append({'uri':'textures/OpenScct_shell_'+('brass' if z0==0 else 'red')+'.png'})
        texture=len(doc.setdefault('textures',[]));doc['textures'].append({'source':image})
        mat=len(doc['materials']);doc['materials'].append({'name':'OpenScct_ReloadShell_'+str(mat),
            'pbrMetallicRoughness':{'baseColorTexture':{'index':texture},'metallicFactor':.7 if z0==0 else 0,'roughnessFactor':.4}})
        attrs={'POSITION':append(doc,data,verts,'VEC3',target=34962),'NORMAL':append(doc,data,normals,'VEC3',target=34962),
               'JOINTS_0':append(doc,data,[[joint,0,0,0]]*len(verts),'VEC4',5123,34962),
               'TEXCOORD_0':append(doc,data,[[.5,.5]]*len(verts),'VEC2',target=34962),
               'WEIGHTS_0':append(doc,data,[[1,0,0,0]]*len(verts),'VEC4',target=34962)}
        parts.append({'attributes':attrs,'indices':append(doc,data,np.array(indices).reshape(-1,1),'SCALAR',5125,34963),'material':mat})
    mesh=len(doc['meshes']);doc['meshes'].append({'name':'ReloadShell','primitives':parts})
    doc['nodes'].append({'name':'ReloadShell_mesh','mesh':mesh,'skin':0})
    doc['scenes'][doc.get('scene',0)]['nodes'].append(len(doc['nodes'])-1)
    for animation in doc['animations']:
        duration=max(float(t[-1]) for _,_,t,_ in tracks(doc,data,animation))
        curve(doc,data,animation,node,'scale',[0,max(duration,1e-6)],[[0,0,0],[0,0,0]])
    doc.get('extras',{}).get('svmap',{})['bones']=len(skin['joints'])
    return node


def spare_magazine(doc, data, mag):
    """Duplicate only the magazine's rigid triangles for the dropped magazine."""
    skin=doc['skins'][0]; source_joint=skin['joints'].index(mag); new_joint=len(skin['joints'])
    _,parents=world_matrices(doc)
    node=len(doc['nodes']);copy=deepcopy(doc['nodes'][mag]);copy.pop('children',None);copy['name']='DroppedMagazine'
    doc['nodes'].append(copy);doc['nodes'][parents[mag]].setdefault('children',[]).append(node)
    skin['joints'].append(node)
    binds=values(doc,data,skin['inverseBindMatrices']).tolist();binds.append(binds[source_joint])
    skin['inverseBindMatrices']=append(doc,data,binds,'MAT4')
    for obj in list(doc['nodes']):
        if obj.get('skin')!=0 or 'mesh' not in obj:continue
        parts=[]
        for part in doc['meshes'][obj['mesh']]['primitives']:
            attrs=part['attributes']
            if 'JOINTS_0' not in attrs:continue
            joints=values(doc,data,attrs['JOINTS_0']);weights=values(doc,data,attrs['WEIGHTS_0'])
            own=np.sum(np.where(joints==source_joint,weights,0),axis=1)>.99
            triangles=values(doc,data,part['indices']).reshape(-1,3)
            triangles=triangles[np.all(own[triangles],axis=1)]
            if not len(triangles):continue
            used,inverse=np.unique(triangles,return_inverse=True);copied={}
            for key,index in attrs.items():
                accessor=doc['accessors'][index];array=values(doc,data,index)[used]
                if key=='JOINTS_0':array[:]=[new_joint,0,0,0]
                if key=='WEIGHTS_0':array[:]=[1,0,0,0]
                copied[key]=append(doc,data,array,accessor['type'],accessor['componentType'],34962)
            part=deepcopy(part);part['attributes']=copied
            part['indices']=append(doc,data,inverse.reshape(-1,1),'SCALAR',5125,34963);parts.append(part)
        if parts:
            mesh=len(doc['meshes']);doc['meshes'].append({'name':'DroppedMagazine','primitives':parts})
            mesh_node=deepcopy(obj);mesh_node.pop('children',None);mesh_node['mesh']=mesh;mesh_node['name']='DroppedMagazine_mesh'
            index=len(doc['nodes']);doc['nodes'].append(mesh_node)
            source_index=doc['nodes'].index(obj)
            if source_index in parents:doc['nodes'][parents[source_index]].setdefault('children',[]).append(index)
            else:doc['scenes'][doc.get('scene',0)]['nodes'].append(index)
    for animation in doc['animations']:
        duration=max(float(t[-1]) for _,_,t,_ in tracks(doc,data,animation))
        curve(doc,data,animation,node,'scale',[0,max(duration,1e-6)],[[0,0,0],[0,0,0]])
    if 'bones' in doc.get('extras',{}).get('svmap',{}):doc['extras']['svmap']['bones']=len(skin['joints'])
    return node


def author(doc, data, profile):
    if doc.get('extras', {}).get('openscct_reload_revision'):
        raise ValueError('Use an unmodified source model when building reloads')
    shell_node=add_shell(doc,data) if profile=='shotgun' else None
    names = {n.get('name'): i for i, n in enumerate(doc['nodes'])}
    pistol = profile not in MAIN_CLIPS
    clip = MAIN_CLIPS.get(profile, 'FirstReloStFd2')
    animation = next(a for a in doc['animations'] if a['name'] == clip)
    if pistol:
        chains = [[next(i for name, i in names.items() if name and name.startswith(s+p))
                   for p in ('_arm_', '_elbow_', '_wrist_')] for s in ('L', 'R')]
        gun, mag, slide = [names[n] for n in ('pistol', 'mag', 'slide')]
    else:
        chains = [[names[f'Bip01 {s} {p}'] for p in ('UpperArm', 'Forearm', 'Hand')] for s in ('L', 'R')]
        gun = next(i for n, i in names.items() if n and n.startswith('corps'))
        mag = names.get('chargeur_HK81', names.get('chargeur'))
        slide = names.get('pompe')
    dropped=spare_magazine(doc,data,mag) if mag is not None and profile!='magnum44' else None
    original = tracks(doc, data, animation)
    duration = max(float(t[-1]) for _, _, t, _ in original)
    idle = pose(doc, original, 0)
    _, parents = world_matrices(doc)
    left, right = chains[0][2], chains[1][2]
    forward = idle[names['FX']][:3, 3] - idle[gun][:3, 3]
    forward[1] = 0; forward /= np.linalg.norm(forward)
    up = np.array([0., 1., 0.]); across = np.cross(forward, up)
    roll, pitch, yaw, pull, travel = STYLE[profile]
    gun_from_hand = np.linalg.inv(idle[right]) @ idle[gun]
    gun_inverse = np.linalg.inv(idle[gun])
    mag_local = gun_inverse @ idle[mag] if mag is not None else None
    slide_local = gun_inverse @ idle[slide] if slide is not None else None
    # Calibrate the fingers/palm to the actual source magazine, independent of
    # the source gun's pose or the new animation's trajectory.
    grasp = pose(doc, original, duration*(.32 if profile=='uzi' else .56))
    reference_mag = names.get('chargeur01', mag)
    grip = np.linalg.inv(grasp[reference_mag]) @ grasp[left] if reference_mag is not None else None
    rack_ref = pose(doc, original, duration*.66)
    rack_grip = np.linalg.inv(rack_ref[slide]) @ rack_ref[left] if slide is not None else None
    shell = profile == 'shotgun'
    revolver = profile == 'magnum44'
    if revolver:
        # Existing part alignment is useful calibration; new paths below drive
        # the cylinder, loader and wrist independently from the old gun motion.
        opened = pose(doc, original, duration*.32)
        cylinder_open = np.linalg.inv(opened[gun]) @ opened[slide]
        loaded = pose(doc, original, duration*.58)
        loader_seated = np.linalg.inv(loaded[slide]) @ loaded[mag]
        loader_grip = np.linalg.inv(loaded[mag]) @ loaded[left]
        # The source wrist target is below the loader. Calibrate the pinch to
        # the actual loader mesh instead of preserving that floating contact.
        skin = doc['skins'][0]
        joint = skin['joints'].index(mag)
        bind = values(doc,data,skin['inverseBindMatrices'])[joint].reshape(4,4).T
        vertices = []
        for mesh in doc['meshes']:
            for part in mesh['primitives']:
                attrs = part['attributes']
                if 'JOINTS_0' not in attrs: continue
                own = values(doc,data,attrs['JOINTS_0'])[:,0] == joint
                vertices.extend(values(doc,data,attrs['POSITION'])[own])
        centre = bind @ np.r_[np.mean(vertices,axis=0),1.]
        tips = [next(i for name,i in names.items() if name and name.startswith(prefix))
                for prefix in ('L_thumb3_', 'L_point3_')]
        pinch = np.linalg.inv(grasp[left]) @ np.r_[np.mean([grasp[i][:3,3] for i in tips],axis=0),1.]
        loader_grip[:3,3] = centre[:3] - loader_grip[:3,:3] @ pinch[:3]
        # The extractor rod is the forwardmost tip of the cylinder assembly.
        # Locate it from the rigid mesh so the pressing fingertip has a real
        # contact target, independent of the weapon's presentation rotation.
        cylinder_joint = skin['joints'].index(slide)
        cylinder_bind = values(doc,data,skin['inverseBindMatrices'])[cylinder_joint].reshape(4,4).T
        cylinder_vertices = []
        for mesh in doc['meshes']:
            for part in mesh['primitives']:
                attrs = part['attributes']
                if 'JOINTS_0' not in attrs: continue
                own = values(doc,data,attrs['JOINTS_0'])[:,0] == cylinder_joint
                cylinder_vertices.extend(values(doc,data,attrs['POSITION'])[own])
        cylinder_vertices = np.asarray(cylinder_vertices)
        cylinder_world = idle[slide] @ cylinder_bind
        points = (cylinder_world[:3,:3] @ cylinder_vertices.T).T + cylinder_world[:3,3]
        along = points @ forward
        ejector_tip = cylinder_bind @ np.r_[np.mean(cylinder_vertices[along > along.max()-.002],axis=0),1.]
        press_node = next(i for name,i in names.items() if name and name.startswith('L_pink3_'))
        press_finger = np.linalg.inv(grasp[left]) @ grasp[press_node][:,3]
    if shell:
        load_ref = pose(doc, original, SHELL_TIMES[0])
        load_grip = np.linalg.inv(load_ref[gun]) @ load_ref[left]
    edited = set(n for chain in chains for n in chain) | {gun}
    if mag is not None: edited.add(mag)
    if slide is not None: edited.add(slide)
    if 'chargeur01' in names: edited.add(names['chargeur01'])
    if shell_node is not None:edited.add(shell_node)
    if dropped is not None:edited.add(dropped)
    if pistol:
        # Reload is a full-weight action over the movement channel. Bake even
        # constant joints: otherwise jump/landing tracks survive wherever the
        # source reload omitted a translation, rotation or scale, separating
        # the wrists from the independently animated weapon.
        edited.update(doc['skins'][0]['joints'])
    fingers = [i for i,n in enumerate(doc['nodes']) if n.get('name','').startswith('L_')
               and any(p in n['name'] for p in ('thumb','point','middle','ring','pink','palm'))]
    times = np.unique(np.r_[np.linspace(0, duration, math.ceil(duration*60)+1),
                             np.array([.12,.14,.28,.38,.44,.56,.58,.66,.73,.82])*duration,
                             list(SHELL_TIMES)+[2.6] if shell else []])
    times = times[times <= duration]
    # Accessors store float32: merge keys that would otherwise become identical
    # after conversion, keeping animation input times strictly increasing.
    times = np.unique(times.astype(np.float32)).astype(float)
    frames = {n: [] for n in edited}
    finger_frames = {n: [] for n in fingers}
    shell_visible=[]
    reach_errors=[]
    released_mag=None
    for time in times:
        f = float(time/duration)
        # A deliberate presentation pose, a short seating impulse and a damped
        # return. No source weapon motion is used here.
        env = smooth([0,.07,.18,.56,.72,.84,.94,1], np.array([0,-.10,1,1,.90,.70,-.06,0]), f)
        if shell:
            env = smooth([0,.12,.22,.87,1], np.array([0,.6,1,1,0]), f)
        # Tip the open cylinder rearward/down for gravity-assisted ejection,
        # then level it again before the speedloader approaches.
        eject_tip = smooth([0,.09,.23,.30,.44,1],np.array([0,0,45,45,0,0]),f) if revolver else 0
        r = rotation(up, math.radians(yaw)*env) @ rotation(across, math.radians(pitch*env+eject_tip)) @ rotation(forward, math.radians(roll)*env)
        right_pose = idle[right].copy()
        right_pose[:3,:3] = r @ right_pose[:3,:3]
        impulse = smooth([0,.53,.56,.60,1], np.array([0,0,-.012,0,0]), f) if not shell else 0
        right_pose[:3,3] += (-across*.055 + up*(.12 if pistol else .18) - forward*pull)*env + up*impulse
        if revolver:
            right_pose[:3,3] -= (forward*.08+up*.035)*(eject_tip/45)
        desired = solve_arm(idle, chains[1], right_pose[:3,3], right_pose[:3,:3],
                            (across*.30-up*.1)*env if not pistol else
                            (across*.14-up*.04)*env if revolver else None)
        desired[gun] = desired[right] @ gun_from_hand
        gun_pose = desired[gun]
        delta = gun_pose @ gun_inverse
        rotated_up, rotated_forward, rotated_across = [r @ v for v in (up, forward, across)]
        held_left = delta @ idle[left]
        if shell:
            seated_hand = gun_pose @ load_grip
            pocket = shift(seated_hand, -up*.19 - across*.11 - forward*.06)
            # Every insert finishes at the same pose as the early-exit frame.
            # This makes loading one shell or six use the same clean return.
            ends = [0, *SHELL_TIMES, 2.6]
            segment = int(np.clip(np.searchsorted(ends,time,side='right')-1, 0,len(ends)-2))
            u = (time-ends[segment])/(ends[segment+1]-ends[segment])
            start_hand = idle[left] if segment == 0 else seated_hand
            hand = path([0,.35,.68,1], [start_hand,pocket,shift(seated_hand,-rotated_up*.055),seated_hand],u)
            if time >= 2.6: hand = mix(seated_hand,idle[left],(time-2.6)/.4)
            desired[slide] = gun_pose @ slide_local
            shell_pose=np.eye(4)
            shell_pose[:3,:3]=np.column_stack([rotated_across,rotated_up,-rotated_forward])
            shell_pose[:3,3]=hand[:3,3]+rotated_forward*.09+rotated_up*.025-rotated_across*.015
            desired[shell_node]=shell_pose
            shell_visible.append(.38 <= u < .96 and time < 2.6)
        elif revolver:
            opening = smooth([0,.08,.16,.76,.82,1],np.array([0,0,1,1,0,0]),f)
            desired[slide] = gun_pose @ mix(slide_local,cylinder_open,opening)
            seated_loader = desired[slide] @ loader_seated
            loader = shift(seated_loader, -rotated_forward*smooth(
                [0,.28,.38,.48,.58,.64,.72,1],np.array([.20,.20,.22,.08,0,.01,.24,.24]),f))
            loader[:3,3] -= up*smooth([0,.28,.38,.48,.58,.68,.78,1],np.array([.12,.12,.12,.025,0,.06,.20,.20]),f)
            # Approach and withdraw on the support-hand side of the firing
            # wrist. Stay on the cylinder axis while seating the cartridges.
            loader[:3,3] -= rotated_across*smooth(
                [0,.28,.38,.50,.58,.64,.73,1],
                np.array([.10,.10,.10,.015,0,0,.12,.12]),f)
            desired[mag] = loader
            loader_hand = loader @ loader_grip
            # Uncup the firing hand sideways before reaching for the open
            # cylinder. Raising the support grip straight up sweeps its palm
            # and fingers through the firing wrist during the first quarter.
            release_hand = shift(held_left, -rotated_across*.10-rotated_up*.04)
            hand_clearance = smooth([0,.14,.20,1],np.array([.12,.12,.095,.095]),f)
            eject_hand = shift(held_left, -rotated_across*hand_clearance+rotated_up*.025+rotated_forward*.04)
            press_hand = held_left.copy()
            press = smooth([0,.24,.28,1],np.array([0,0,.018,.018]),f)
            press_hand[:3,3] = (desired[slide] @ ejector_tip)[:3] - rotated_forward*press - press_hand[:3,:3] @ press_finger[:3]
            close_hand = shift(desired[slide] @ (np.linalg.inv(idle[slide]) @ idle[left]), -rotated_across*.025)
            hand = path([0,.07,.16,.22,.24,.28,.38,.58,.73,.78,.82,.92,1],
                        [idle[left],release_hand,eject_hand,press_hand,press_hand,press_hand,
                         loader_hand,loader_hand,loader_hand,close_hand,close_hand,held_left,idle[left]],f)
        else:
            seated_mag = gun_pose @ mag_local
            distance = smooth([0,.12,.14,.23,.32,.38,.48,.56,.60,1],
                              np.array([0,0,.015,travel,travel,travel,.055,0,0,0]),f)
            magazine = shift(seated_mag, -rotated_up*distance - rotated_across*(distance*.28))
            # Angle the spare magazine during the approach; straighten before
            # insertion. Its mesh and gripping hand share this exact transform.
            tilt = smooth([0,.20,.34,.43,.53,1],np.array([0,0,.25,.20,0,0]),f)
            magazine[:3,:3] = rotation(rotated_forward,tilt) @ magazine[:3,:3]
            desired[mag] = magazine
            if f < .14:
                old_mag=seated_mag.copy()
            else:
                if released_mag is None:released_mag=seated_mag.copy()
                age=(f-.14)*duration
                old_mag=shift(released_mag,-up*(.45*age+2.5*age*age)+across*.06*age)
                old_mag[:3,:3]=rotation(across,age*3) @ released_mag[:3,:3]
            desired[dropped]=old_mag
            magazine_hand = magazine @ grip
            # Hold the bolt/slide back, then release it at the mechanical cue.
            bolt = smooth([0,.08,.14,.60,.64,.66,.70,1],np.array([0,0,.026,.026,.038,0,0,0]),f)
            desired[slide] = shift(gun_pose @ slide_local, -rotated_forward*bolt)
            rack_hand = desired[slide] @ rack_grip
            hand = path([0,.10,.14,.50,.56,.59,.63,.66,.72,.88,1],
                        [idle[left],magazine_hand,magazine_hand,magazine_hand,magazine_hand,
                         shift(magazine_hand,-rotated_up*.035),rack_hand,rack_hand,
                         shift(rack_hand,-rotated_across*.08),held_left,idle[left]],f)
        desired.update(solve_arm(idle,chains[0],hand[:3,3],hand[:3,:3],
                                (-across*.25-up*.1)*env if not pistol else
                                (-across*.20-up*.06)*env if revolver else None))
        reach_errors.append(float(np.linalg.norm(desired[left][:3,3]-hand[:3,3])))
        # Copy the reference finger grip as local curves, with relaxed fingers
        # during the reach and recovery. Skin weights/mesh topology are retained.
        finger_blend = smooth([0,.10,.18,.70,.88,1],np.array([0,.35,1,1,.2,0]),f)
        for node in fingers:
            parent = parents[node]
            a = np.linalg.inv(idle[parent]) @ idle[node]
            b = np.linalg.inv(grasp[parent]) @ grasp[node]
            finger_frames[node].append(decompose(mix(a,b,finger_blend)))
        if 'chargeur01' in names:
            desired[names['chargeur01']] = idle[names['chargeur01']].copy()
        # Resolve unedited ancestor motion from the source so authored world
        # targets stay fixed even on rigs with an animated spine/root.
        old = pose(doc,original,time)
        def current(node):
            if node in desired:return desired[node]
            parent=parents.get(node)
            if parent is None:return old[node]
            return current(parent) @ np.linalg.inv(old[parent]) @ old[node]
        if f == 0 or f == 1:
            desired = {n: old[n] for n in edited}
        for node in edited:
            parent = parents.get(node)
            local = np.linalg.inv(current(parent)) @ current(node) if parent is not None else current(node)
            frames[node].append(decompose(local))
    if max(reach_errors)>.002:
        raise ValueError(f'{profile}: hand target exceeds arm reach by {max(reach_errors):.4f} m')
    animation['channels'] = [c for c in animation['channels'] if c['target']['node'] not in edited | set(fingers)]
    for node, poses in {**frames, **finger_frames}.items():
        for i,p in enumerate(('translation','rotation','scale')):
            if node == dropped and p == 'scale':
                vals=[[int(.14<=t/duration<.44)]*3 for t in times]
            elif not shell and not revolver and node==mag and p=='scale':
                vals=[[int(t/duration<.14 or t/duration>=.38)]*3 for t in times]
            elif node == shell_node and p == 'scale':
                vals=[[int(v)]*3 for v in shell_visible]
            elif node == names.get('chargeur01') and p == 'scale':
                vals = [[0,0,0]]*len(times)
            elif revolver and node == mag and p == 'scale':
                vals = [[int(.38 <= t/duration < .73)]*3 for t in times]
            else:vals = [v[i] for v in poses]
            curve(doc,data,animation,node,p,times,vals)
            if p=='scale' and (node in (shell_node,dropped,mag) or node==names.get('chargeur01')):
                animation['samplers'][-1]['interpolation']='STEP'
            doc['accessors'][animation['samplers'][-1]['input']].update(min=[0.0],max=[duration])
    if not pistol:
        # Long primary receivers and magazines otherwise cross the near plane
        # when canted. Present the whole first-person rig farther from the eye;
        # moving its root keeps arm lengths and every prop contact unchanged.
        root=doc['skins'][0]['joints'][0]
        translation=[]
        for time in times:
            f=float(time/duration)
            env=smooth([0,.07,.18,.56,.72,.84,.94,1],np.array([0,-.10,1,1,.90,.70,-.06,0]),f)
            if shell:env=smooth([0,.12,.22,.87,1],np.array([0,.6,1,1,0]),f)
            old=pose(doc,original,time)
            local=old[root] if root not in parents else np.linalg.inv(old[parents[root]])@old[root]
            translation.append(local[:3,3]+forward*.18*env)
        animation['channels']=[c for c in animation['channels'] if not (c['target']['node']==root and c['target']['path']=='translation')]
        curve(doc,data,animation,root,'translation',times,translation)
        doc['accessors'][animation['samplers'][-1]['input']].update(min=[0.0],max=[duration])
    doc.setdefault('extras',{})['openscct_reload_revision']={'version':2,'profile':profile,'clips':[clip],
        'method':'independent contact-driven weapon, hand and mechanism timelines',
        'max_hand_reach_error_m':max(reach_errors),
        'max_hand_reach_error_phase':float(times[int(np.argmax(reach_errors))]/duration)}
    return [clip]






