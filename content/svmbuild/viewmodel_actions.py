"""Camera-space actions for the custom guns, preserving their authored grips.

The original clips supply names and durations only. Copying individual joint
displacements between differently proportioned arm rigs stretches the wrists.
These actions move a coherent held pose and solve the free arm for handling.
"""
import math
import numpy as np
from .sidearm_models import world_matrices
from .pistol_arms import tracks, pose, curve, decompose
from .reload_motion import solve_arm, smooth

MELEE = {'FirstAtakCrFd2', 'FirstAtakChit2'}
CHARGE = {'FirstAtakChFd2', 'FirstAtakChEd2'}
OVERRIDE = MELEE | CHARGE | {'FirstReloStFd1', 'FirstButtStFd2'}


def author(doc, data, reference, reference_data, held=None):
    if held is None:
        idle = next(a for a in doc['animations'] if a['name'] == 'FirstWaitStFd2')
        held = pose(doc, tracks(doc, data, idle), 0)
    _, parents = world_matrices(doc)
    local = [np.linalg.inv(held[parents[i]]) @ m if i in parents else m for i,m in enumerate(held)]
    names = {n.get('name'):i for i,n in enumerate(doc['nodes'])}
    gun = next((i for n,i in names.items() if n and n.startswith('Main_j_')), names.get('pistol', 57))
    animated = set(doc['skins'][0]['joints']) | {gun}
    hand_nodes = []
    for node in animated:
        parent = parents.get(node)
        while parent is not None:
            if parent == 10:
                hand_nodes.append(node)
                break
            parent = parents.get(parent)
    original = {a['name']:a for a in doc['animations']}
    doc['animations'] = [a for a in doc['animations'] if a['name'] not in OVERRIDE]
    for clip in reference['animations']:
        name = clip['name']
        if name in original and name not in OVERRIDE: continue
        source = tracks(reference, reference_data, clip)
        duration = max(float(t[-1]) for _,_,t,_ in source)
        times = np.linspace(0, duration, max(2,math.ceil(duration*30)+1))
        frames = {node:[] for node in animated}
        for time in times:
            f = time / max(duration,1e-9)
            move = np.zeros(3); yaw = pitch = roll = 0.
            if name in CHARGE or name == 'FirstAtakChit2':
                weight = 1-smooth([0,1],np.array([0,1]),f) if name == 'FirstAtakChEd2' else 1
                move = np.array([.035,-.04,.025])*weight
                yaw, roll = -.12*weight, -.08*weight
                if name == 'FirstAtakChFd2': move[1] += math.sin(f*math.tau)*.004
            if name in MELEE:
                wind = smooth([0,.16,.35,.48,.72,1],np.array([0,-.22,1,.75,.12,0]),f)
                move += np.array([.035,.012,-.07])*wind
                yaw += .42*wind; pitch = -.10*wind
                if name == 'FirstAtakChit2':
                    recovery = 1-smooth([0,.55,1],np.array([0,1,1]),f)
                    move *= recovery; yaw *= recovery; roll *= recovery
            launcher = name == 'FirstReloStFd1'
            handling = smooth([0,.12,.3,.72,1],np.array([0,.3,1,1,0]),f) if launcher else 0
            if launcher:
                move = np.array([.025,-.025,.015])*handling; roll = -.10*handling
            if name in {'FirstWalkStFd2','FirstWaitStLt2','FirstWaitStRt2'}:
                move = np.array([0,math.sin(f*math.tau)*.004,math.sin(f*math.tau)*.003])
            if name in {'FirstLandStFd2','FirstLandStHi2','FirstJumpStFd2','FirstFallStFd2'}:
                move[1] = -.016*math.sin(f*math.pi)
            if name.startswith('FirstPain') or name == 'FirstElecStFd2':
                move = np.array([-.012,-.008,.006])*math.sin(f*math.pi)
            if name in {'FirstGrbdStBg2','FirstReanStFd2'}:
                move[1] = -.07*math.sin(f*math.pi/2)
            if name == 'FirstButtStFd2':
                move[0] = .035*math.sin(f*math.pi)
            cy,sy,cp,sp,cr,sr = math.cos(yaw),math.sin(yaw),math.cos(pitch),math.sin(pitch),math.cos(roll),math.sin(roll)
            r = np.array([[cy,0,sy],[0,1,0],[-sy,0,cy]]) @ np.array([[cp,-sp,0],[sp,cp,0],[0,0,1]]) @ np.array([[1,0,0],[0,cr,-sr],[0,sr,cr]])
            delta = np.eye(4); delta[:3,:3] = r
            pivot = held[gun][:3,3]; delta[:3,3] = move+pivot-r@pivot
            desired = {n: delta@held[n] for n in animated}
            if launcher:
                # The support hand dips below the weapon, without crossing the
                # firing wrist or rolling its forearm through a half turn.
                base = [delta@m for m in held]
                hand = base[10].copy(); hand[:3,3] += np.array([-.025,-.10,-.035])*handling
                desired.update(solve_arm(base,(7,8,10),hand[:3,3],hand[:3,:3],twist_node=9))
                # Moving the wrist while retaining the old world-space finger
                # targets lengthens the palm/first finger links by the entire
                # handling offset. Carry every hand descendant with the wrist.
                follow = desired[10] @ np.linalg.inv(base[10])
                desired.update({node: follow @ base[node] for node in hand_nodes})
            def current(node):
                if node in desired:return desired[node]
                parent = parents.get(node)
                return current(parent)@local[node] if parent is not None else local[node]
            for node in animated:
                parent = parents.get(node)
                frames[node].append(decompose(np.linalg.inv(current(parent))@current(node) if parent is not None else current(node)))
        animation = {'name':name,'channels':[],'samplers':[]}
        for node,poses in frames.items():
            for i,path in enumerate(('translation','rotation','scale')):
                curve(doc,data,animation,node,path,times,[p[i] for p in poses])
        doc['animations'].append(animation)
