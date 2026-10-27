"""Writes re/ghidra/annotations/physics.json: the names, namespaces and short comments this study added, per
program, in the svm.ghidra.annotations/1 layout (one entry per line). Entries already present in the tracked
editor.json / game.json are left out. Plain Python (no Ghidra needed): python re/ghidra/scripts/physics/write_annotations.py
"""

import json
import os

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', '..'))
ANN = os.path.join(REPO, 're', 'ghidra', 'annotations')

EDITOR_FUNCS = [
    (0x112157c0, 'ASBasePawn', 'startNewPhysics'),
    (0x11204510, 'ASBasePawn', 'FindArmsGE'),
    (0x11207de0, 'ASBasePawn', 'CheckFence'),
    (0x111e95d0, 'ASPlayerController', 'CheckLedgeArms'),
    (0x111ec210, 'ASPlayerController', 'CheckLadderArms'),
    (0x111ebf10, 'ASPlayerController', 'CheckPipeArms'),
    (0x111ebcd0, 'ASPlayerController', 'CheckZipArms'),
    (0x111ea1e0, 'ASPlayerController', 'CheckFenceTopArms'),
    (0x111ed7a0, 'ASPlayerController', 'CheckHOHArms'),
    (0x111e8d70, 'ASPlayerController', 'CheckBarrierArms'),
    (0x1111fc00, 'ULevel', 'GetGEInBox'),
    (0x11307140, 'FGEPartition', 'QueryBox'),
    (0x11309fa0, 'FGENodeX', 'Query'),
    (0x1130a100, 'FGENodeY', 'Query'),
    (0x1130a270, 'FGENodeZ', 'Query'),
    (0x11309d80, 'FGELeaf', 'Query'),
    (0x113045c0, 'FGEPole', 'FGEPole'),
    (0x11305830, 'FGEFence', 'FGEFence'),
    (0x11305740, 'FGEHandOverHand', 'FGEHandOverHand'),
    (0x11305260, 'FGELedge', 'FGELedge'),
    (0x11304b00, 'FGELadder', 'FGELadder'),
    (0x11304720, 'FGEPipe', 'FGEPipe'),
    (0x113043b0, 'FGEZipLine', 'FGEZipLine'),
    (0x113059b0, 'FGE', 'SerializeRecord'),
    (0x11305180, 'FGESegment', 'Distance'),
    (0x113049e0, 'FGEThreePoint', 'Distance'),
    (0x113056c0, 'FGESegment', 'Connected'),
    (0x11305920, 'FGEThreePoint', 'Connected'),
    (0x11305650, 'FGESegment', 'Equals'),
    (0x113050d0, 'FGEThreePoint', 'Equals'),
    (0x11305570, 'FGESegment', 'ClosestPoint'),
    (0x11305060, 'FGEThreePoint', 'ClosestPoint'),
    (0x113044a0, 'FGEZipLine', 'ClosestPoint'),
    (0x11304610, 'FGEPole', 'ClosestPoint'),
    (0x11305220, 'FGESegment', 'GetPoints'),
    (0x11304aa0, 'FGEThreePoint', 'GetPoints'),
    (0x11304600, 'FGEPole', 'Kind'),
    (0x11305870, 'FGEFence', 'Kind'),
    (0x11305780, 'FGEHandOverHand', 'Kind'),
    (0x113051a0, 'FGELedge', 'Kind'),
    (0x11304a00, 'FGELadder', 'Kind'),
    (0x11304770, 'FGEPipe', 'Kind'),
    (0x113043f0, 'FGEZipLine', 'Kind'),
    (0x110dc370, 'APawn', 'ShouldTrace'),
    (0x110a7af0, 'AActor', 'IsBlockedBy'),
    (0x110a77d0, 'AActor', 'IsOverlapping'),
    (0x110a7a80, 'AActor', 'GetPrimitive'),
    (0x10f57b10, 'AActor', 'ToWorld'),
    (0x10f57aa0, 'AActor', 'ToLocal'),
    (0x110ba2b0, 'AActor', 'GetCylinderExtent'),
    (0x110ba330, 'AActor', 'IsBasedOn'),
    (0x110dc350, 'APawn', 'IsHumanControlled'),
    (0x111b47a0, 'FStaticMeshCollisionCheck', 'Init'),
    (0x111b48a0, 'FStaticMeshLineCheck', 'FStaticMeshLineCheck'),
    (0x111b72b0, 'FStaticMeshBoxCheck', 'FStaticMeshBoxCheck'),
    (0x111b6c50, 'FStaticMeshLineCheck', 'CheckNode'),
    (0x111b7590, 'FStaticMeshBoxCheck', 'CheckNode'),
    (0x10fdd4e0, 'FVector', 'SafeNormal'),
    (0x10e7b190, 'FVector', 'Normalize'),
    (0x10e62030, 'FVector', 'Size'),
    (0x110ba420, 'FVector', 'IsNearlyZero'),
    (0x10fdcc70, 'FVector', 'Rotation'),
    (0x10eb2a70, 'FVector', 'TransformPointBy'),
    (0x10e930e0, 'FVector', 'TransformVectorBy'),
    (0x10f32d60, '', 'GetRotationCoords'),
    (0x10fde500, '', 'PointDistToSegment'),
    (0x10fde7a0, '', 'ClosestPointOnSegment'),
    (0x10fde8c0, '', 'ClosestPointOnLine'),
    (0x110823b0, '', 'PointsAreNear'),
    (0x10fdeef0, '', 'FixedTurnRate'),
    (0x10ed0230, 'FCheckResult', 'FCheckResult'),
    (0x111fa300, '', 'eventCanBeDropAttacked'),
    (0x111fa340, '', 'eventDropAttack'),
    (0x111fa3a0, '', 'eventMyFalling'),
    (0x111fa3d0, '', 'eventTopSynchro'),
    (0x111fa400, '', 'eventClientStopRM'),
    (0x111fa4a0, '', 'eventStickyBlast'),
    (0x111fa4d0, '', 'eventNoGE'),
    (0x111fa500, '', 'eventTopPipeCrouch'),
    (0x111fa530, '', 'eventOrthoPipeH'),
    (0x111fe460, '', 'eventFalling'),
    (0x111ff280, '', 'eventMyNotifyLanded'),
    (0x111ff460, '', 'eventChangeLedge'),
    (0x111e4050, '', 'eventAutoFeetLedge'),
    (0x111e4080, '', 'eventHandOverHand'),
    (0x111e40b0, '', 'eventWaistLedge'),
    (0x111e40e0, '', 'eventFeetLedge'),
    (0x111e4110, '', 'eventTopLadder'),
    (0x111e4140, '', 'eventTopPipe'),
    (0x111e4170, '', 'eventNarrowLadder'),
    (0x111e41a0, '', 'eventLedge'),
    (0x111e8c40, '', 'eventPipe'),
    (0x111e8ca0, '', 'eventZipLine'),
    (0x111e8d00, '', 'eventFenceTop'),
    (0x1117b7e0, '', 'eventNotifyHitWall'),
    (0x1117b850, '', 'eventMayFall'),
    (0x110ba620, '', 'eventEncroachingOn'),
    (0x110ba550, '', 'eventBulletWentTru'),
    (0x1120c970, '', 'eventBump_SBasePawn'),
    (0x110ba910, '', 'eventBump'),
]

GAME_FUNCS = [
    (0x10acaaf0, 'ASBasePawn', 'startNewPhysics'),
    (0x10ace9f0, 'ASBasePawn', 'performPhysics'),
    (0x10ad29a0, 'ASBasePawn', 'setPhysics'),
    (0x10ac6b40, 'ASBasePawn', 'physWalking'),
    (0x10ac5db0, 'ASBasePawn', 'physWalkingSimulated'),
    (0x10ace4d0, 'ASBasePawn', 'MycalcVelocity'),
    (0x10a2bce0, 'APawn', 'calcVelocity'),
    (0x10a2c460, 'APawn', 'stepUp'),
    (0x10a2c9f0, 'AActor', 'stepUp'),
    (0x10a2ced0, 'APawn', 'processHitWall'),
    (0x10a2b4b0, 'APawn', 'physicsRotation'),
    (0x109b0630, 'AActor', 'TwoWallAdjust'),
    (0x10ad3dc0, 'ASBasePawn', 'physFalling'),
    (0x10ae4390, 'ASBasePawn', 'bCanLand'),
    (0x10ae4500, 'ASBasePawn', 'processLanded'),
    (0x10acac40, 'ASBasePawn', 'physRootMotion'),
    (0x10ad38d0, 'ASBasePawn', 'physFallingInRootMotion'),
    (0x10ad65e0, 'ASBasePawn', 'EndRootMotion'),
    (0x10ad10c0, 'APawn', 'physOnPipe'),
    (0x10acf860, 'APawn', 'physLinear'),
    (0x10acf3e0, 'ASBasePawn', 'physOnVerticalPlane'),
    (0x10ac9400, 'APawn', 'physLedge'),
    (0x10ac8d20, 'ASBasePawn', 'physVandamme'),
    (0x10ad2220, 'ASBasePawn', 'physRepositionning'),
    (0x10ad2750, 'AActor', 'physFollow'),
    (0x10ad2a40, 'APawn', 'physOnLadder'),
    (0x10acff50, 'APawn', 'physOnPipeH'),
    (0x10a2e960, 'APawn', 'physFlying'),
    (0x10ae2f70, 'ASBasePawn', 'TestTopPipe'),
    (0x10ae3270, 'ASBasePawn', 'TestTopLedge'),
    (0x10ae5260, 'ASBasePawn', 'CheckIfBarrier'),
    (0x10accc10, 'ASBasePawn', 'UnCrouch'),
    (0x10ac90a0, 'ASBasePawn', 'FindArmsGE'),
    (0x10aceb90, 'ASBasePawn', 'CheckFence'),
    (0x10aa4420, 'ASPlayerController', 'CheckLedgeArms'),
    (0x10aa7650, 'ASPlayerController', 'CheckLadderArms'),
    (0x10aa7360, 'ASPlayerController', 'CheckPipeArms'),
    (0x10aa7140, 'ASPlayerController', 'CheckZipArms'),
    (0x10aa8f90, 'ASPlayerController', 'CheckHOHArms'),
    (0x10aa4ff0, 'ASPlayerController', 'CheckFenceTopArms'),
    (0x10aa3540, 'ASPlayerController', 'CheckBarrierArms'),
    (0x10aa8b00, 'ASPlayerController', 'CheckLedgeGrabFeet'),
    (0x10aa8640, 'ASPlayerController', 'CheckLadderFeet'),
    (0x10aa8080, 'ASPlayerController', 'CheckPipeFeet'),
    (0x10ab0f10, 'ASPlayerController', 'bTestTopLadder'),
    (0x10ab2dd0, '', 'eventLedge'),
    (0x10ab2df0, '', 'eventWaistLedge'),
    (0x10ab2e20, '', 'eventAutoFeetLedge'),
    (0x10a1d200, 'ULevel', 'SingleLineCheck'),
    (0x10a1d400, 'ULevel', 'MultiLineCheck'),
    (0x10a1b780, 'ULevel', 'MoveActor'),
    (0x10a1c8d0, 'ULevel', 'CheckEncroachment'),
    (0x10a42f30, 'UModel', 'LineCheck'),
    (0x10a0f320, 'UPrimitive', 'LineCheck'),
]

EDITOR_COMMENTS = [
    (0x112157c0, 'Physics mode switch: returns below 0.0003 s or after 7 iterations; see spec/native/physics-dispatch.md'),
    (0x11204510, 'Arms-zone GE search: best and second GE within m_CurrentArmsRadius; see spec/native/physics-ge-queries.md'),
    (0x111e95d0, 'LG arms test: AutoFeetLedge when walking, else WaistLedge then Ledge (two 35x35x87.5 box checks, 0x2df)'),
    (0x111fc2d0, 'Ground velocity: friction 8, no AccelRate clamp, GroundSpeed cap'),
    (0x112025e0, 'Walking: wall pre-probe 0xd6, 0.05 s sub-steps, floor probe 36, step 34'),
    (0x11213090, 'Falling: gravity -1500, half-step integration, 417 horizontal and 2500 total caps, drop-attack homing'),
    (0x111b7c70, 'Hull tree unless TRACE_ShadowCast or no hull; UseSimple*Collision flags are not read'),
    (0x110be450, 'Adds 0x400 unless 0x20000; skips BSP hits on poly flag 0x80 (and 0x1) with 0x80100'),
]

GAME_COMMENTS = [
    (0x10acaaf0, 'Physics mode switch (game build); jump table at 0x10acabf4'),
    (0x10aa4420, 'LG arms test (ledge, waist ledge, auto feet ledge); see spec/native/physics-ge-queries.md'),
]


def known(fn):
    d = json.load(open(os.path.join(ANN, fn), encoding='utf-8'))
    return d, {int(f['address'], 16) for f in d['functions']}


def section(base_doc, funcs, comments, have):
    fl = sorted(f for f in funcs if f[0] not in have)
    ns = sorted({f[1] for f in fl if f[1]})
    return {
        'format': 'svm.ghidra.annotations/1', 'id': base_doc['id'], 'program': base_doc['program'],
        'sha256': base_doc['sha256'], 'imageBase': base_doc['imageBase'],
        'counts': {'namespaces': len(ns), 'functions': len(fl), 'labels': 0, 'comments': len(comments)},
        'namespaces': [{'path': n, 'kind': 'class'} for n in ns],
        'functions': [{'address': '0x%08x' % a, 'namespace': n, 'name': nm} for a, n, nm in fl],
        'labels': [],
        'comments': [{'address': '0x%08x' % a, 'type': 'plate', 'text': t} for a, t in sorted(comments)],
    }


def dump(doc):
    out = ['{', '  "format": "svm.ghidra.annotations/1",', '  "id": "physics",',
           '  "note": %s,' % json.dumps(doc['note']), '  "programs": [']
    secs = []
    for s in doc['programs']:
        lines = ['    {']
        for k in ('format', 'id', 'program', 'sha256', 'imageBase'):
            lines.append('      %s: %s,' % (json.dumps(k), json.dumps(s[k])))
        lines.append('      "counts": %s,' % json.dumps(s['counts']))
        keys = ('namespaces', 'functions', 'labels', 'comments')
        for i, k in enumerate(keys):
            last = i == len(keys) - 1
            items = s[k]
            if not items:
                lines.append('      %s: []%s' % (json.dumps(k), '' if last else ','))
                continue
            lines.append('      %s: [' % json.dumps(k))
            lines.append(',\n'.join('        ' + json.dumps(x) for x in items))
            lines.append('      ]' + ('' if last else ','))
        lines.append('    }')
        secs.append('\n'.join(lines))
    out.append(',\n'.join(secs))
    out.append('  ]')
    out.append('}')
    return '\n'.join(out) + '\n'


def main():
    ed, ed_have = known('editor.json')
    gm, gm_have = known('game.json')
    doc = {'note': 'Per-program sections in the svm.ghidra.annotations/1 layout; merge each into '
                   'annotations/<program id>.json.',
           'programs': [section(ed, EDITOR_FUNCS, EDITOR_COMMENTS, ed_have),
                        section(gm, GAME_FUNCS, GAME_COMMENTS, gm_have)]}
    text = dump(doc)
    json.loads(text)
    with open(os.path.join(ANN, 'physics.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    print([(s['id'], s['counts']) for s in doc['programs']])


if __name__ == '__main__':
    main()
