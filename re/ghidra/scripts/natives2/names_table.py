"""Names, signatures and short comments the natives2 study added (plain data, no game text).

Each entry: (address, 'Namespace::Name', signature or None). COMMENTS: (address, plate text). Used by
write_annotations.py (re/ghidra/annotations/natives2.json) and, through sigs.py, to name a fresh copy.
Entries already present in another tracked annotations file are left out of the JSON by the writer.
"""

EDITOR_FUNCS = [
    (0x10f3a9c0, 'FFrame::Step', 'void Step(void * Context, void * Result)'),
    (0x10e931b0, 'GetRotationCoordsInv', None),
    (0x10eb2b10, 'FCoords_TransformVector', None),
    (0x10fde6a0, 'PointDistToSegmentT', 'float PointDistToSegmentT(FVector * A, FVector * B, FVector * P, float * T)'),
    (0x10f64a30, 'FVector::SizeSquared', None),
    (0x10fa3370, 'appFrand', None),
    (0x111e4ca0, 'ASPlayerController::WallJumpProbe', None),
    (0x111e4df0, 'ASPlayerController::WallJumpPickDirection', None),
    (0x111c5e00, 'ASGamePlayObject::ProcessDamage', 'void ProcessDamage(int Damage)'),
    (0x111bde00, 'CastToSPlayerController', None),
    (0x111bf270, 'CallEventByName', None),
    (0x111e16d0, 'ASProjFrag::CallEventByName', None),
    (0x111cb720, 'ASMineProx::CallEventByName', None),
    (0x111cc8b0, 'ASMineLaser::CallEventByName', None),
    (0x1121fe70, 'ASObjective::Desactivate', 'void Desactivate(void)'),
    (0x112205f0, 'ASMission::Desactivate', 'void Desactivate(void)'),
    (0x111ce1e0, 'ASObjectiveTrigger::Desactivate', 'void Desactivate(void)'),
    (0x1121ffa0, 'ASObjective::Reset', 'void Reset(void)'),
    (0x1121f960, 'ASObjective::ScheduleReset', None),
    (0x111ce0a0, 'ASObjectiveTrigger::GetTriggerTime', 'float GetTriggerTime(void)'),
    (0x112203b0, 'ASMission::eventDoBroadcastLocalizedMessage', None),
    (0x11220310, 'ASMission::eventNotifyTaken', None),
    (0x111d3420, 'ASMagicEvent::UpdateTimer', None),
    (0x111d2ff0, 'ASMagicEvent::ProcessQueue', None),
    (0x111d2f10, 'ASMagicEvent::FireEvent', None),
    (0x111d66e0, 'ASWeaponDefense::GetFireStart', None),
    (0x110be6a0, 'ULevel::CheckSlice', None),
]

GAME_FUNCS = [
    (0x10a85f90, 'ASDamageVolume::Tick', None),
    (0x10a86240, 'eventbIsDamageVolumeSensitive', None),
    (0x10904030, 'appSeconds', None),
    (0x10a8fc90, 'ASGamePlayObject::ProcessDamage', None),
]

EDITOR_COMMENTS = [
    (0x110a7520, 'Writes DrawType (SetDrawType), not DrawScale'),
    (0x1123fb80, 'MSVC float to int conversion, truncates toward zero'),
    (0x111c5e00, 'GPO damage: HP, blast to every visible actor in DamageActorRange, shake, spawns, best damage stage'),
    (0x111d2ff0, 'Fires every queued event whose time has come, in queue order, on the 0.05 s timer'),
    (0x1120ff60, 'Spy health refill lives here (HealthRefill, MinTimeHealthRefill, NbPointsRefillable), not in TickNative'),
]

GAME_COMMENTS = [
    (0x10a85f90, 'Damage volume tick: real-time period (appSeconds), every touching actor, no Super.Tick'),
]
