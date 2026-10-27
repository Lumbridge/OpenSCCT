"""Names, signatures and plate comments the actions study gives to the editor and game builds.

Plain data, read by a30_names.py (applies them in the study's project copy) and by write_annotations.py
(exports re/ghidra/annotations/actions.json). Each function entry: address -> (namespace, name, signature
or None, thiscall). The names are our reading of each function's behaviour; UE2 names where one exists.
"""

EDITOR = {
    # ExecuteAction and the natives it calls
    0x11215980: ('ASBasePawn', 'ExecuteAction', 'void f(int Action)', True),
    0x11189250: ('AActor', 'PlayAnim', 'void f(int Channel, int Sequence, float Rate, float TweenTime, int bLoop, int bBackward, int bContinueAtFrame, int bMirror)', True),
    0x111fd020: ('ASBasePawn', 'setPhysics', 'void f(byte NewPhysics, AActor * NewFloor, void * NewFloorV)', True),
    0x11206990: ('ASBasePawn', 'Crouch', 'void f(int bClientSimulation)', True),
    0x111f9f80: ('ASBasePawn', 'SetLedgeShimmyAnims', 'void f(void)', True),
    0x111f9fa0: ('ASBasePawn', 'SetFenceClimbAnims', 'void f(void)', True),
    0x111fa090: ('ASBasePawn', 'SetPipeHHandsAnims', 'void f(void)', True),
    0x111fa0b0: ('ASBasePawn', 'SetPipeHLegsAnims', 'void f(void)', True),
    0x111fa170: ('ASBasePawn', 'UpdateIsLeftPart', 'void f(void)', True),
    0x111fa1f0: ('ASBasePawn', 'EndGrabbedRestore', 'void f(void)', True),
    0x111fa790: ('ASBasePawn', 'eventNotifyQuickLaunch', 'void f(void)', True),
    0x111fa5b0: ('ASBasePawn', 'eventAnimateBTW', 'void f(void)', True),
    0x111fa5e0: ('ASBasePawn', 'eventRemoveBTWArmControl', 'void f(void)', True),
    0x111fa700: ('ASBasePawn', 'eventevStickyShockSfx', 'void f(int Arg)', True),
    0x111fa7c0: ('ASBasePawn', 'eventEvSetWeaponLoaded', 'void f(int Arg)', True),
    0x111fa730: ('ASBasePawn', 'eventExecuteActionForGoggle', 'void f(int Arg)', True),
    0x111fa6d0: ('ASBasePawn', 'eventBeginStickySkin', 'void f(void)', True),
    0x111fa670: ('ASBasePawn', 'eventRestoreSkins', 'void f(void)', True),
    0x10f9db90: (None, 'OutputLogf', None, False),
    0x10e0c820: (None, 'StructIsChildOf', 'int f(void * Parent)', True),
    0x10f68a90: (None, 'StructIsChildOf2', 'int f(void * Parent)', True),
    # small vector helpers used by the end-offset maths
    0x10e7a0e0: ('FVector', 'ScaledBy', 'void * f(void * Out, float Scale, void * V)', False),
    0x10e1bd20: ('FVector', 'operator_add', 'void * f(void * Out, void * V)', True),
    0x10e61f10: ('FVector', 'operator_sub', 'void * f(void * Out, void * V)', True),
    0x10e61f50: ('FVector', 'operator_mul', 'void * f(void * Out, float Scale)', True),
    0x10eb2750: ('FVector', 'operator_subassign', 'void * f(void * Out, void * V)', True),
    0x10eb2720: ('FVector', 'operator_neg', 'void * f(void * Out)', True),
    0x10eb9c30: ('FVector', 'IsZero', 'int f(void)', True),
    # animation
    0x110a6190: ('AActor', 'IsAnimating', 'int f(int Channel)', True),
    0x11137e70: ('AActor', 'UpdateMeshInstance', None, True),
    0x110a6d10: ('AActor', 'UpdateSimAnim', None, True),
    0x11208a60: ('ASBasePawn', 'NotifyAnimEnd', None, True),
    0x111005a0: ('USkeletalMeshInstance', 'SetBlendParams', 'int f(int Stage, float BlendAlpha, float InTime, float OutTime, int BoneName, int bGlobalPose)', True),
    0x11102b50: ('USkeletalMeshInstance', 'BlendToAlpha', 'void f(int Stage, float TargetAlpha, float TimeInterval)', True),
    0x11102730: ('USkeletalMeshInstance', 'SetChannelRateScale', 'void f(int Channel, float Scale)', True),
    0x110fed00: ('USkeletalMeshInstance', 'IsAnimating', 'int f(int Channel)', True),
    0x110ff140: ('USkeletalMeshInstance', 'GetAnimFrame', 'float f(int Channel)', True),
    0x110ff0d0: ('USkeletalMeshInstance', 'GetAnimSequence', None, True),
    0x110fec90: ('USkeletalMeshInstance', 'ValidateAnimChannel', 'int f(int Channel)', True),
    0x110febd0: ('USkeletalMeshInstance', 'MatchRefBone', 'int f(int BoneName)', True),
    0x110ff8f0: ('USkeletalMeshInstance', 'PlayAnim', 'int f(int Channel, int Sequence, float Rate, float TweenTime, int bLoop, int bBackward, int bContinueAtFrame, int bMirror)', True),
    0x11102870: ('USkeletalMeshInstance', 'SetAnimFrame', 'void f(int Channel, float Frame, int UnitFlag)', True),
    0x111029a0: ('USkeletalMeshInstance', 'StepBlendAlpha', 'void f(int Channel, float Target, float MaxStep)', True),
    0x11102840: ('USkeletalMeshInstance', 'GetBlendAlpha', 'float f(int Channel)', True),
    0x111056c0: ('USkeletalMeshInstance', 'UpdateAnimation', 'int f(float DeltaSeconds)', True),
    0x111015f0: ('USkeletalMeshInstance', 'UpdateRootMotion', None, True),
    0x11109a50: ('USkeletalMeshInstance', 'BuildPose', None, True),
    # hit zones
    0x111090a0: ('USkeletalMesh', 'LineCheck', None, True),
    0x110ac5d0: ('AActor', 'TraceBoneCheck', None, True),
    0x11103ff0: (None, 'SerializeMeshCollisionPoints', None, False),
    0x11104150: (None, 'SerializeMeshCapsules', None, False),
    0x11104250: (None, 'SerializeMeshSpheres', None, False),
    # input
    0x111e6460: ('ASPlayerController', 'ComputeInputValues', 'void f(void)', True),
    # camera
    0x111f71d0: ('ASPlayerCam', 'InitCamParam', 'void f(int Mode)', True),
    0x111f4e50: ('ASPlayerCam', 'UpdateViewImpl', 'void f(int Pitch, int Yaw, int Roll)', True),
    0x111f4df0: ('ASPlayerCam', 'ResettingRotationImpl', 'int f(void)', True),
    0x111f70d0: ('ASPlayerCam', 'BehindViewCalcView', None, True),
    0x111f70f0: ('ASPlayerCam', 'OwnViewCalcView', None, True),
    0x111f6af0: ('ASPlayerCam', 'ThirdPersonCalcView', None, True),
    0x111f56a0: ('ASPlayerCam', 'SolveFollowingCamera', None, True),
    0x111f4be0: ('ASPlayerCam', 'SmoothAngle', None, True),
    0x10fea400: (None, 'BiasCurve', None, False),
    0x10fdefd0: (None, 'MoveTowardVector', None, False),
    0x10fe0af0: (None, 'MoveTowardScalar', None, False),
}

GAME = {
    # the game build (link-time optimised): same functions, other addresses and vtable slots
    0x10ad6840: ('ASBasePawn', 'ExecuteAction', 'void f(int Action)', True),
    0x10a23590: ('AActor', 'PlayAnim', 'void f(int Channel, int Sequence, float Rate, float TweenTime, int bLoop, int bBackward, int bContinueAtFrame, int bMirror)', True),
    0x10ad29a0: ('ASBasePawn', 'setPhysics', 'void f(byte NewPhysics, AActor * NewFloor, void * NewFloorV)', True),
    0x109b6de0: ('USkeletalMeshInstance', 'SetBlendParams', None, False),
    0x10aaa910: ('ASPlayerController', 'ComputeInputValues', None, False),
}

COMMENTS = {
    'editor': {
        0x11215980: 'SBasePawn action switch, 329 entries at 0x1121d618; see spec/native/execute-action.md',
        0x110ff8f0: 'Channel start: notify flush of the cut clip, loop/tween/backward/mirror set-up; see spec/native/anim.md',
        0x111056c0: 'Frame advance per channel (4 passes), notifies, AnimEnd, PlayAnimAfter queue; see spec/native/anim.md',
        0x11109a50: 'Pose build: channels in index order from their start bone, mirror through Pawn.BoneMirrors, root lock',
        0x111015f0: 'Root bone of channel 0 in the lock frame; EndRootMotion when unlocked at the clip end',
        0x112008a0: 'Walk blender: channels 1 to 13 from the relative velocity and the walk set; see spec/native/anim.md',
        0x110ac5d0: 'TraceBone core: flags 0x40097, then a pill pass over pawns within 3 x CollisionHeight',
        0x111090a0: 'Pill check: capsules and spheres between collision points, highest priority then nearest',
        0x111e6460: 'Buttons to stick values (6000 x GetSpeedMode), mouse to look values; see spec/native/input.md',
        0x111f37d0: 'Use edge, input values, iMove bits (2100 / 3000 / 4500 thresholds); see spec/native/input.md',
        0x111f56a0: 'Following camera solver: start, offset, twig, target Z, ideal distance, collision; see spec/native/camera.md',
        0x111f71d0: 'Fills m_camParam for one of the 35 camera modes (parent copy then overrides)',
        0x111f3e50: 'Calc view dispatch by controller state name; see spec/native/camera.md',
    },
    'game': {
        0x10ad6840: 'SBasePawn action switch (game build); same 280 case groups as the editor',
        0x10aaa910: 'Buttons to stick values (game build)',
    },
}
