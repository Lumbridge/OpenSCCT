# Original PS2 vial pickup

This opt-in behavior applies to `bPS2Objective` extraction vials. It uses the original PS2 pickup state flow with the runtime's semantic action names. PC action ordinals and PS2 action ordinals differ.

## Rules

1. `SPickupInteraction.PostBeginPlay` calls its base and then fixes its actual collision cylinder to radius 50 and height 50. Its source base has no `PostNetBeginPlay` sizing override. A remote entry retains these dimensions when its owner reference arrives after its initial network bunch.
2. A living player's Use request enters the generic pickup goto state, source name ID546928. It does not enter the PC flag/drop-zone goto state.
3. On the input machine, the stand target is the interaction owner's location plus its forward axis times 80. Set targetZ to current pawnZ; add 27.5 when crouched. Facing is the owner's rotation with yaw increased by 32768.
4. The source reposition action 151 runs at 180 units/second. It reads the controller's boolean at native offset 0xC28, mask 1, into `bUseAdditionnalPhysicsForRepos`. That controller boolean is source global name 855937. Controller startup clears it; the pickup goto state leaves it false. The resulting native reposition moves in XYZ with world collision disabled for that move. Its existing arrival distance is less than 1, and its vertical dead band is 5.
5. Arrival enters the pickup state, source name ID546929. It chooses the crouched low/forward action at a 20-unit height threshold, or the standing action at 30. The local owning controller requests the selected hand item. Pickup EndState attaches it to `B R Thigh`, relative location `(-10,0,10)` and rotation `(16000,0,0)`. Direct pickup of a dropped tube uses the same source attachment.
6. A wall hit cancels to standing walking. A bump ignores `SComputerObjectiveTrigger` and `SFlag`; other actors cancel. The pickup state's animation end returns to the walking action for the current stance.

The source computer goto state deliberately sets the same controller boolean true around action 151 and then clears it. It therefore keeps walking collision. This port scopes the different reposition behavior to the verified vial pickup flow.

## Evidence

- PS2 `COM_ONL`: interaction initialization 0xD63B6, pickup sizing 0xD641B; controller goto BeginState 0xECF13, arrival 0xECCCE, wall 0xECD74, bump 0xECDD9, pickup BeginState 0xECBA8.
- PS2 native ExecuteAction 0x29B4C0: jump table 0x498B70, action 151 at0x2A1DD0. It reads controller 0xC28 mask 1 and writes pawn 0x319 mask8.
- PS2 native reposition 0x297340: the same pawn bit selects walking or XYZ movement; arrival check 0x297920 remains less than 1.
- The complete 310-property controller layout starts after superclass size 0x650. Its 65 consecutive final booleans occupy words 0xC20,0xC24,0xC28; the final property 1668/name 855937 is 0xC28 mask 1. End 0xC2C aligns to registered size 0xC30. Native BoolProperty.Link 0x164120 proves packed masks and word overflow. Native struct alignment includes Vector and BigServerMoveStruct; Rotator and DamageInfos use4-byte alignment.
- Controller PostBeginPlay explicitly clears property 1668. Computer goto BeginState temporarily sets it, executes 151, then clears it. Pickup goto makes no such assignment.
- Source carry name 853638 uniquely matches `B R Thigh`: source quaternion, position and parent match retained `SPerso.ATT_01_PS2` bone 36 and `DEF_01_PS2` bone 21 exactly. The current runtime ATT_01/DEF_01 right-thigh frame has the same position and parent; quaternion components differ by at most 2 ULP.
- Source pickup actions 229..232 at 0x2A3D7C, 0x2A3DC0, 0x2A3E04, 0x2A3E48 each make one animation call on channel 0, rate 1, tween 0.1, then exit. Their native names 45685, 45687, 45689, 45691 match the `SPerso.PRO` name pairs `PickCrAlFd0`, `PickCrAlUp0`, `PickStAlFd0`, `PickStAlUp0`. No extra timed case-hack stage appears in those actions.

## Verification

### Delivery guidance

While a spy carries a PS2 vial, the HUD shows its live authored `DropZone` destinations until the objective is taken. This uses the same destination list and completion condition as `SFlag.CheckDropZone`, without calling that mutating function from presentation. PS2 delivery volumes implement `Touch` and do not run the PC suitcase interaction that maintains `bTriggering`; that field alone cannot determine their marker visibility. An unrelated destination, an unladen viewer or a merc does not receive this extra marker.

Synthetic tests exercise actual Use-menu initialization and source state handoff. Network tests run profiles 100 and 300, resolve the replicated interaction owner, verify the effective 50 by 50 cylinder and authority/client reposition choice, and retain the original mesh and carry attachment. The `ps2-pickup` Godot smoke presses and releases the actual Use key on `DTK02P`'s tight cabinet objective, waits for native reposition and animation completion, and requires authoritative pickup and replicated thigh attachment. `SVM_PS2_PICKUP_CROUCHED=1` selects its crouched case. No direct pickup or synthetic arrival call is used in that map smoke.
