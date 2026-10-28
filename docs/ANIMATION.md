# Animation

How a pawn is animated, from the simulation's channels to the drawn pose. The rules are in
`spec/native/anim.md`, `spec/native/ANSWERS-anim.md` (the decoded detail: PlayAnim, the update passes, the walk
blender, the tween cache, AnimateBTW) and `spec/native/execute-action.md` (which action plays what). Ports keep the
original names.

## Layers

| Layer | Where | What |
|---|---|---|
| Channel model | `src/OpenScct.Game/Movement/PawnAnimation.cs` | the 25 channels of a pawn's mesh instance and the engine's channel natives |
| Walk blender | `src/OpenScct.Game/Animation/PawnMovementAnimation.cs` | `ASBasePawn::UpdateMovementAnimation`: channels 1 to 15 every tick |
| Action animation | `src/OpenScct.Game/Animation/ActionAnimation.cs` | the channel side of ExecuteAction the action ports leave out |
| Notifies | `src/OpenScct.Game/Animation/PawnAnimNotifies.cs` | clip notifies from the converted packages, fired by the update |
| Root motion | `src/OpenScct.Game/Movement/PawnRootMotion.cs` | the root's movement and turn for PHYS_RootMotion |
| Aim | `src/OpenScct.Game/Animation/MeshAimIK.cs`, `game/Scripts/Views/AimPose.cs` | the upper body turned toward the aim (AimAt) |
| Arm aim | `src/OpenScct.Game/Animation/ArmAim.cs`, `MeshBoneDirections.cs` | an upper arm turned toward the view (TurnHead's arm cases) |
| Pose | `game/Scripts/Views/ChannelPoseApplier.cs`, `SimPawnView.cs` | the client's pose from the channels |
| View models | `game/Scripts/Views/ViewModelPose.cs`, `FirstPersonView.cs`, `PawnAttachments.cs` | first-person arms, third-person weapons |
| Difftest | `tests/OpenScct.DiffTest/Anim` | the channels against the real game, frame by frame |

## Channels (simulation)

`SBasePawn.PawnAnim` is a `PawnAnimation`: channels 0 to 24 (anim.md channel table). Each `PawnAnimChannel` holds
the record the engine keeps: sequence and clip, frame (normalised over N, negative while tweening in), AnimRate,
AnimLast, tween rate, old rate, base rate, loop, backward, mirror, notify flags, blend alpha, blend times, start bone
(a bone name; the client resolves it), the timed blend, the queued clip and the tween weight.

Natives, with the original names:

| Member | Native |
|---|---|
| `PlayAnim(channel, clip, rate, tween, loop, backward, continueAtFrame, mirror)` | USkeletalMeshInstance::PlayAnim (A1) |
| `Play(channel, clip, rate, tween, loop, mirror, backward)` | the same, the argument order the movement ports use |
| `TweenAnim`, `PlayAnimAfter` | TweenAnim, PlayAnimAfter |
| `SetBlendParams(channel, alpha, in, out, bone, globalPose)` | AnimBlendParams (channel 0 is not written) |
| `BlendToAlpha`, `StepBlendAlpha`, `SetBlendAlpha` | AnimBlendToAlpha, the walk blender's step and set |
| `SetAnimSequence`, `ClearSequence`, `CopyAnimation` | SetAnimSequence (frame and rate kept), CopyAnimation |
| `SetChannelRateScale` (`SetPlayRate`), `SetAnimFrame` (`SetFrame`), `EnableChannelNotify`, `SetMirror` | the channel helpers |
| `ZeroMovementAlpha(from, to, step)` | APawn::ZeroMovementAlpha |
| `IsAnimating`, `IsAnimBackward`, `GetAnimFrame`, `GetBlendAlpha`, `GetSequence`, `ClipRate` | the queries |
| `Advance(dt, animEnd, inRootMotion)` | UpdateAnimation (A2) |
| `UpdateTweenCache()` | the pose build's tween cache (A4) |
| `Restore(...)` | a recorded channel state (difftests only) |

`SetSequence` is the movement's own helper (a loop at rate 0 until `SetPlayRate`), kept for its callers.

The update, once per tick at the start of the pawn's tick (ANSWERS-fidelity.md F3): channels in index order, up to
4 passes each. A frame of exactly 0 is only nudged to 1e-4; notifies fire in time order and split the advance; a
one-shot holds at AnimLast and raises AnimEnd (pawn, then controller); a loop raises AnimEnd when it crosses AnimLast
and restarts at 0 (then 1e-4) when it reaches 1; in root motion a clip holds at its last frame for a tick. A negative
rate scales with the owner's speed (at least 0.3 per second). `PawnAnimation.Owner` is the animated actor (notifies
and the speed need it; the pawn's tick sets it).

## Walk blender

`PawnMovementAnimation.Update` runs in the pawn's post-tick (`SBasePawn.UpdateMovementAnimation`). It runs the
simulation part of the script event TurnHead (`iYawPitchFromView`, `iDesiredYawPitch`, the IK history), then by
state: dead or lying (everything fades), a blend into channel 0 (the roll), the split-jump and horizontal-pipe aim
blends, the back-to-wall and ledge shimmy, the grabbed merc, the pipe and ladder climb step, the modes that zero the
walk channels, the feet-ledge reset, falling (the roll fades past frame 0.65), standing (turn in place on 2 and 3) and
moving (the walk set on 4 to 8, copies on 9 to 13, the slow walk, the run fraction, channel 5's rate, every walk
channel at channel 5's frame). Last it sets OldRotYaw (the post-tick does) and runs the tween cache for a drawn
pawn. The first call fills channels 2 to 13 with AWaitSt0 loops.

## Actions

`SBasePawn.ExecuteAction` runs `ActionAnimation.Begin`, the action port (`PawnActions`, `TraversalActions`,
`MovesetActions`, `InteractionActions`), then `ActionAnimation.End`:

- `Begin` is ExecuteAction's prologue: BlendChangeTime 0.25, no blend into channel 0; it also runs a pose build
  first (the tween weights at landings need it; estimated).
- `End` adds the channel work of the case: the walk sets of AnimateWalking (`WalkSet`), the blend params and rate
  scales of the draw channel 14, the roll and landing channel 15 and the no-run overlay 1, the set refresh after a
  roll (`SetAnimSetAfterRolling`, `SetAnimSetAfterRollingUp`), the ledge and back-to-wall blend times.
- `PlayJumpStart`, `PlayRoll` (the run cycle's foot, the blend into channel 0) and `AnimateBTW` (the shimmy set and
  the hold loop) are called by the action ports.
- `End` also gives the overlays the ports play their blend: the elbow (8 to 10) on channel 19 from B Spine1, the hand
  interaction and the mine (13 to 16, 290) on channel 24 from B Spine1, the palm (38) from 0.1 to 1 over 0.2 s.
- Any other action goes to `CombatActionAnimation.Execute`, which plays the cases no port has: the weapon overlay
  (6, 291 to 297 on channel 21), the drop-attack and inert landings and the inert fall (7, 92, 101, 106), the
  stand-up (11, AnimateStandUp: the merc's wake clip from its fall pose, the spy on its back through root motion),
  the reanimate overlay (12), the grab and grabbed clips (18, 20 to 37, 327, with the partner's half where the table
  has it), deaths (40 to 46, PlayDeath: the spy through root motion, the merc walking), the cough (47, 48), hits
  (49 to 65, PlayDamage: the special hits and a moving pawn's head flinch from B Head, at rest the body hit on 17 or
  the electric loop on 23, and the merc's first-person pain), the binoculars (73, 74), the back-to-wall fire
  transitions and holds (183 to 192), throws (230 to 233, 251), blasts and their wake-ups (239 to 243, 249, 250),
  the held switch (283 to 285), the grab from a ledge (312 to 314), the grab repositioning (19, 321), terminate
  (317 to 319) and the counter grab (325). Not done: the grabbed merc's PHYS_FollowParent (the mode is not decoded), the case events (skins, shock
  sounds, RemoveBTWArmControl).
- `SBasePawn.AnimEnd` runs `PawnAnimEnd.Channel`: channels 16, 17, 18, 19, 21, 22 and 24 blend out as the script
  does (17 over 0.3 s, restoring IK_Type), the draw channel turns off and moves a back-to-wall fire transition on to
  its hold, channel 1 hands back to the run channel, and channel 0 chains the split-jump fall, the hack loop, the
  corner turn back, the terminate holds, the spy's lying pose XxxxDnAlBk0 after a fall-down clip, and the fall after
  a grab from a ledge.

The combat goldens (local/difftest/golden-combat, `anim` records) confirm the choices: the reload on c21
ReloStSpFd2, hits at rest on c17 (SA_DMG_HEAD plays PainStAlCB0, the chest-back clip), the spy's deaths by direction
(XxxxStAlBk0 when shot from the front), the grab pairs (GrabStAlBg0 with GrbdStAlBg0, GrabStAlKo0 with
GrbdStAlKo0, GrabStAlXxb with GrbdStAlKo0), the merc's GrbdStAlWkA and the spy's WakeStSpBk0 stand-ups, StrkCrAlBk0
then XxxxDnAlBk0 after a charge, the blast clips and their wake-ups (merc WakeStSpBk2, spy WakeStSpBk0), and the
elbow on c19. A rifle shot plays no SA_FIRE.

## Notifies

`PawnAnimationSource.FromContent` reads every sequence's notifies (`AnimNotify_Sound`, `AnimNotify_StopSound`,
`AnimNotify_Script`). Sounds play at the owner, stop sounds stop on it, script notifies go to the level's
`AnimNotifyHandlers` (`AnimNotifyHandlers.Of(level).Register(name, handler)`), else to the ported default (the
footsteps PlayFootStepLeft and PlayFootStepRight). Areas register their own notifies (the gadgets register
AnimNotify_Interact); the draw notifies (NotifyDrawUndrawWeapon and kin) stay with their owners' call sites until
those move to the notify.

## Root motion

`PawnRootMotion` gives physics the root's movement (in the lock frame) and its turn: the root keys' yaw about the
mesh's vertical axis (`PawnClip.RootYaw`), applied by physics with lock bit 2. The end placement (EndRootMotion)
takes the pawn back to rRotBeforeRM + rRotAfterRM.

The root position of a clip (`PawnClip.RootAt`) is its keyed root track when it has one (`PawnClip.RootTrack`, the
`rootTrack` of `svm.anim/1`: key frames and positions, linear between keys, held before the first key and after the
last; 75 spy and 19 merc clips, among them the ladder, wall jump, death and wake clips), else its root bone. Both are
absolute (the key as the package stores it), so a clip change while the root is locked takes its first delta from
the previous clip's last root position (ANSWERS-moveset.md findings 1 and 2; goldens moveset.spy-ladder-up,
spy-ladder-down, merc-ladder-down and spy-wall-jump, frame 36).

## Pose (client)

`SimPawnView` reads all 25 channels after each client tick and draws in between with the frames interpolated while
the same clip runs on, across a loop's wrap too (a fall of more than half a cycle in one tick is taken as the wrap and
interpolated through 1 back to 0; before, every loop drew a tick ahead and then held for a tick, a hitch each walk
cycle). `ChannelPoseApplier.Apply` builds the pose in the original order (anim.md rule 3): channel 0 at
weight 1, every other channel with a clip and an alpha over the bones from its start bone down, rotation slerp and
translation lerp by the alpha. A channel tweening in blends from the last drawn pose by the tween step (A4: 0 on the
first draw of a tween, then 1 - frame / last frame). Backward clips sample from the end (A5). Mirrored channels swap
the L and R bones (estimated reflection). In root motion the root's translation stays where the move started and,
with lock bit 2, its turn since then is taken out (the actor's yaw carries it). `SimPawnView.TryGetBone` and
`PawnViews.TryGetBone` give a bone's drawn world transform; `PoseCameraBones` answers `ICameraBones` from them.

The merc's head camera (the script's `m_camera.AttachCamToBone(true, 'B Head', offset)`) puts the first-person view
on that bone: on a ladder (`MercLadderCamera`), in the berserk spin, and in the combat states that raise
`CombatFx.HeadCamera` (grabbed by a spy and its release states, the charge knock-back, the KO fall and lying, the
finishing move). `CombatFx.Notify` applies that event and `FirstPersonView` (bBehindView off) on the machine that
raises them, which for the owner's states is the owner's own machine; detaching (stand-up, release, death, a new pawn)
also clears bFirstPersonWithPawn. With bBehindView off the own body stays hidden and the view rides the head; in the
finishing move (bBehindView and bFirstPersonWithPawn on) the body is drawn. `CombatFx.WeirdEffect` (SetWeirdEffect)
is an audio effect in the original (the native calls the audio subsystem), not a screen effect, and has no listener.

## Aim (upper body)

An armed pawn's upper body follows its aim (spec/native/aim-ik.md), on every machine that draws it:

- `PawnMovementAnimation.AimWeapon`, the IKT_WEAPONLOADED case of TurnHead: with a weapon loaded and no weapon
  transition, `AimAt(AAFULL)` toward the merc's iDesiredYawPitch or the spy's iYawPitchFromView, from the pawn's
  facing, 90 degrees each way. Both values replicate, so bots and other players aim too.
- `MeshAimIK` is the mesh instance's part (`PawnAnimation.Aim`): the request, and at each animation update (first,
  with the update's time) the AAFULL pair (yaw, pitch) steps toward 0.85 of the aim at 8 radians per second, the
  smaller change slowed so both arrive together; without a request the angles return to 0 at the same speed.
- The pose (`ChannelPoseApplier.AimBone`, `AimRotation`, set by `SimPawnView`) turns B Spine2 (bone 4) by the pitch
  about the horizontal axis across the facing, then by the yaw about the vertical, after the channels are blended; the
  arms, head and held weapon follow as its children.

`--svm-smoke=play-aim` checks it through the keys: the spy draws the Shocker (E) and looks up and down with the mouse;
the drawn right hand's elevation from B Spine2 must change by at least half of 0.85 of the view's pitch change
(before: 0.0 degrees for a 42.5 degree change; now 21.1). It also checks that the drawn hand turns sideways the way
of the aim (89 % of the frames with a sideways aim over 8.6 degrees) and reports the merc bots' aim.
`tests/OpenScctGame.Logic.Tests/Views/AimIKTests.cs` checks the step, the limits and that every armed merc bot proxy on
the client asks for the aim toward its owner's desired rotation. A rifle shot plays no body clip in the original
(the combat goldens have no SA_FIRE); what other players see of a shot is this aim and the third-person muzzle flash.

### Arm aim

TurnHead's other cases turn an upper arm toward the view (spec/native/aim-ik.md rule 5): firing round a corner from
back to wall (the right arm round a left corner, the left arm round a right one; a fixed holster pose while the corner
draw plays backward), firing while holding a grabbed body, firing from a pipe hang (by one hand or by the legs) and
firing braced in a split jump.

- `ArmAim` (`src/OpenScct.Game/Animation/ArmAim.cs`, called from `PawnMovementAnimation.TurnHead`) picks the case,
  steps the bone's world rotation toward the view plus the pose's offset by at most 100000 (split jump 150000)
  rotator units per second per axis, and leaves it on `PawnAnimation.Directions` (`MeshBoneDirections`, the mesh
  instance's SetBoneDirection controllers). It releases the arms when no case holds one; `RemoveBTWArmControl`
  (SA_BTW_OUT) releases the right arm and forearm. The step starts from where the arm is drawn (the level's
  `ICameraBones`, the drawn poses); a server has no pose and takes the target at once (nothing reads it there).
- `SimPawnView.SetDirections` turns each controller's world rotation into the skeleton's space and
  `ChannelPoseApplier.Directions` gives the bone that rotation after the spine aim, so the forearm, hand and weapon
  follow. It runs for every drawn pawn: the inputs (IK_Type, BTW, PipHFire, iYawPitchFromView, the pawn's yaw) are
  replicated and the split-jump flag comes from a clip notify, so other players' pawns aim their arms too.

## Weapons and view models

- First person (merc): `FirstPersonView` poses the weapon's own mesh (SWeaponFirst.1DEF_01, the arms and rifle) from
  the weapon's `ViewModelAnim` channels through `ViewModelPose`: a one-shot runs over the time the simulation gives
  it (its play rate kept), backward plays sample from the end, loops run at the clip rate times their LoopAnim rate
  (`ViewModelAnim.Rate`). AnimateWaiting loops FirstWaitStFd2 at 1 and FirstWalkStFd2 at `clamp(0.006 x speed, 0, 3)`.
- Third person: `PawnAttachments` draws the merc's SWeaponDefenseDummy (SWeaponFirst.M160) on "B R Hand" while the
  weapon is loaded and on "HOLSTER" otherwise, and the spy's pistol (SWeaponFirst.p190) on the same bones, posed by
  its own clips. The attachment takes the bone's frame with no offset, as the script sets.

## Difftest

`tests/OpenScct.DiffTest/Anim` replays every movement and traversal scenario with goldens (DtReplay, recorded start) and
compares the pawn's channels with the reference run's record each frame: sequence, frame, AnimRate, blend alpha and,
in the traversal records (`ach`, channels 0 to 15), the tween weight and tween rate. The sim's channels start from the
reference's frame -1. Frames after the sim's movement leaves the reference (controller state or physics) are not
compared. Gates: channel 0 and every drawn channel (alpha above 0 on either side) match on every compared frame;
channels at alpha 0 are reported only (the game can show stale records there). A tween weight equal to the sim's
previous frame passes (the recorder can sample before the pose build).

```powershell
dotnet test --project tests/OpenScct.DiffTest/OpenScct.DiffTest.csproj --filter-class "*AnimDiffTests"
$env:SVM_DIFFTEST_ANIM_DUMP = "*"     # or a scenario id list: per-frame channel dumps
```

Reports: `local/difftest/anim/report.md` and one JSON per scenario (never tracked).

## Clips in play: the gap table

What the original plays in normal spy and merc play (execute-action.md, the goldens, the SPerso.PRO and SPerso.Def
packages: 339 spy and 144 merc sequences, all named by the ported code or its generated defaults) against what our
client drew. "Before" is the build Ryan tested (fa7fd88); "now" is after this wave. The play check is the
`play-anim` smoke (below) and `tests/OpenScct.Game.Tests/Animation/ActionClipTests.cs`.

| Group | Original clips (examples) | Channel, by | Before | Now |
|---|---|---|---|---|
| Idle | WaitStAlFd0, WaitStSpFd2, waitCrAlFd0, waitCrSpFd2 | c0, AnimateWalking | drawn | drawn (the packages hold no idle fidgets) |
| Walk, run, sneak, strafe, back | WalkStAlFd0/Rt0, JoggStAlFd0, WalkCrAlFd0, JoggCrAlFd0, WalkStSpFd2, JoggStAlFd2 | c2 to 13, walk blender | goldens 100 %, drawn | same; bones move 95 to 120 uu a step in play |
| Turn in place | the TurnAnim set | c2, c3 | ported (golden after the ledge pull-up) | same |
| Crouch and stand | WaitCrAlBg0/Ed0, WaitCrSpBg2/Ed2 | c0, 81 to 84 | drawn | drawn; the crouched mesh height is the crouch work |
| Jumps, falls, landings | jumpstalfdl/r, JumpStAlFL2, fallstaldn0, landStAlLo0/Hi0, landCrAlQt0 | c0, 90 to 105 | drawn | drawn |
| Roll | RollStAlFdL/R, RollStJpFdL/R | c15 | golden | same |
| Traversal | Ledg*, PipV*, PipH*, Ladd*, Zipp*, Fenc*, Back*, Wljp*, Splt* | c0, c15 | goldens; ladder and wall jump off by root motion | root track: the four ladder and wall-jump goldens pass |
| Draw, holster | DrawStSpBg2/Ed2, DrawCrSpBg2/Ed2 | c14 | drawn | drawn |
| Merc weapon | ReloStSpFd2 (S, SU), ReloStSpFd1, ReloStSpFd3 | c21 from B Spine1 | not played | played, blended out at the end |
| Hits | Pain{St,Cr}Al{CB,CF,CL,CR,AL,AR,LL,LR}{0,2}, PainStHs*, GoglStAlDn2 flinch, PainStAlEc2 | c17, c23 | not played | played |
| Deaths | Xxxx{St,Cr}Al{Fd,Bk,Lt,Rt,Dn}{0,2}, XxxxStAlAk0, LandStXxDn2, then XxxxDnAlBk0 | c0 | not played (the pawn stood) | played; the spy's through root motion |
| Knock-out, wake | WakeSt*/WakeCr*, GrbdStAlWkA | c0, 11 | not played | played |
| Grab | GrabStAlBg0/Nt0/Xxb/xxa/Ko0/XxC, GrbdStAl*, GrabLg*Rail, GrbdStLgRail | c0 | walk sets only | played |
| Melee | AtakStAlFd0/2, AtakStAlDn0, StrkSt/CrAlBk*, AtakStAlTu, AtakChAlEd2 | c19, c0 | elbow at alpha 0 | drawn |
| Terminate | AtakDnMmBg2/Nt2/Ko2, AtkdDnMmBg0/Nt0/Ko0/Ed0 | c0 | not played | played |
| Blasts | BlstStAlDn2/It2/Ed2, LandStXxFd2, the wake clips | c0 | not played | played |
| Cough (merc) | CaughStSpFd2 | c20 from B Head | not played | played |
| Gadgets | MineSt/CrAlBg0/Ed0, PalmSt/CrAlNt0/2, ThroStAlMix, ThroSt/CrAl*H, GoglSt*Dn2, WaitSpSpMix | c24, c22, c0, c18 | mine and palm at alpha 0 | drawn |
| Interactions | ButtStAIFd0/2, ButtStAINt0, KbrdStAl*, HackStAl*, Pick*, ExtrStAl* | c24, c0 | hand interaction at alpha 0; held switch not played | drawn, played |
| Emotes | none in the character packages | - | - | - |
| Merc first person | FirstWaitStFd2, FirstWalkStFd2, FirstJumpStFd2, FirstShooStFd2, FirstReloStFd2 | view model | drawn; the walk loop while standing | drawn; the idle loop while standing |
| Weapon aim (spy and merc) | AimAt AAFULL of IKT_WEAPONLOADED | B Spine2 turn | not in the pose | drawn (play-aim) |
| Arm aim (corner, grab, pipe and split-jump fire) | TurnHead arm cases | B R / B L UpperArm | not in the pose | drawn (not checked in play yet) |
| Bots and other players | everything above, through the proxy action playback | proxies | c0 and c4 only (a merc bot drew JoggStAlFd0, which DEF_01 lacks) | walk, crouch, jump, land and goggle channels drawn (play-anim, 3 bots); armed bots turn their upper body to their aim |

## Other players' pawns

On a client, another player's pawn (a bot or a remote player) is a simulated proxy animated as in the original
(spec/net/proxy-pawns.md, spec/native/net-proxy.md rules 5 to 9), by `OpenScct.Game.Animation.ProxyAnimation`:

- `SBasePawn.PostNetBeginPlay`: a pawn without a controller starts with the walk set of its stance
  (InitializeBasicActions: AnimateWalking 75, 76, 85 or 86 by crouch and by the unarmed spy).
- The server relays the owner's action batches (docs/NETCODE.md); `GetNewActions` stores a newer batch, and the
  proxy tick (`TickSimulated`, run first by `PawnProxyReceiver.Tick`) plays it through `SBasePawn.ExecuteAction`, so
  the clips, the walk sets and the stance follow the owner's. The crouch actions on a proxy only set
  `bIsCrouched`; the owner's zone configuration then moves the proxy by its ZCDecal offset and sets the cylinder
  height and PrePivot (crouched: PrePivot.Z raised by the height change, so the mesh stays on the floor).
- The walk blender runs on the proxy from its smoothed velocity as on any pawn.

Crouching (any pawn with authority or autonomous): `SBasePawn.Crouch` sets PrePivot to the default plus the height
change and the stand-ups restore it (execute-action.md rule 5.6), so the mesh drawn at Location + PrePivot keeps its
feet on the floor while the cylinder's centre drops.

`ProxyAnimationTests` (tests/OpenScctGame.Logic.Tests) plays SteeE with three bots through the play session and checks
every bot proxy's walk set, clips, channel motion and crouched placement. Two smokes look at the drawn pawns:

```
tools\godot.7.2-stable\Godot_v4.7.2-stable_mono_win64_console.exe --path game -- --svm-smoke=play-crouch
tools\godot.7.2-stable\Godot_v4.7.2-stable_mono_win64_console.exe --path game -- --svm-smoke=play-proxies
```

`play-crouch` crouches and walks crouched at the bound keys and checks the drawn spy's lowest foot bone against the
floor in the steady crouched stance and the crouched clips (WalkCrAlFd0, JoggCrAlFd0, waitCrAlFd0); `play-proxies`
watches three bots and checks that their drawn bones move while they move, with their walk set's clips, and that
their feet rest on the floor.

## Play check (play-anim smoke)

`--svm-smoke=play-anim` plays SteeE as the launchers do (Play Level rules, the listen server on its own thread, wall
clock) and drives the moveset with the bound keys through `KeyPilot` (docs/TESTING-PLAY.md): idle, look, walk, run,
back, sideways, crouch, crouch walk, stand, jump, running jump, roll, then draw, walk, fire and holster (spy) or fire
and reload (merc). Each frame it reads the channels of every drawn pawn and of the merc's view model and records, per
step and channel, the sequences, how many frames they were drawn, whether the animation set and the mesh have them,
and per step how far the drawn bones moved against the skeleton, the lowest bone against the feet and the
controller states. The local pawn and the view model fail the run (a stand-in or a clip the mesh lacks drawn, or the
pawn moving while no drawn bone moves); the bots' findings are notes. `SVM_ANIM_SIDE` (spy, merc), `SVM_ANIM_BOTS`
(default 3), `SVM_ANIM_WAIT` (seconds standing among the bots at the end, for hits and deaths) and `SVM_PLAY_SHOTS`
(a folder, absolute or under local/) choose the run. The table goes to
`local/anim-play/<side>.md`.

```
tools\godot\4.7.2-stable\Godot_v4.7.2-stable_mono_win64_console.exe --path game -- --svm-smoke=play-anim --svm-smoke-timeout=380
```

## Open

- LookAt and the AAVERT, AAHOH and AABTW turns are not in the pose; AimAt AAFULL is (Aim above), and so are
  TurnHead's arm rotations (Arm aim above). The space of the pose build's bone coords at the turn is taken as the
  actor's (aim-ik.md open question 1); SetBoneDirection's space 1 as the bone's world frame (open question 3).
- bGlobalPose and the exact mirror reflection (anim.md open question 1); lock bit 4.
- The pose build's timing is emulated (after the tick for a drawn pawn, after the advance for the local merc in
  first person, before each action).
- The root turn is the yaw of the root key's X axis; keys within 60 degrees of upright keep the previous yaw.
- traversal.spy-zip-line-29: the movement leaves the reference at frame 0, so no channels are compared.
- The root track's rotation keys (LaddStAlBu0 turns 180 degrees in its track) are not read; the turn still comes
  from the root bone. The four root-track goldens pass with it.
- The merc's view model: s_Selected.Tick now runs AnimateWaiting every tick as the script does, so the arms play
  FirstWaitStFd2 while the merc stands (play-anim merc: 419 of 419 idle frames, before 0;
  `tests/OpenScctGame.Logic.Tests/Views/MercArmsTests.cs`: 0 of 301 still ticks before). The walk loop's rate
  `clamp(|V| x 0.006, 0, 3)` is ported (Clip timing below). Not ported: the side-step blend of that tick
  (FirstWaitStLt2 and Rt2 on channels 1 and 2 at alpha |side|, 0.2 s blends; at rest their alpha is 0), and the
  view model loops raising AnimEnd when they wrap.
- The grabbed merc's PHYS_FollowParent (cases 31, 32) is not set: the physics mode is not decoded.
- Bots: a merc proxy drew c4 with JoggStAlFd0 (a spy clip DEF_01 lacks) at alpha 1 and no walk channels; the
  proxy playback (Other players' pawns) now gives it the merc's walk set. The merc sets of AnimateWalking leave
  RunAnim at JoggStAlFd0 (channel 4 at alpha 0 while the merc walks).
- Crouch transitions: a crouch or stand-up moves the cylinder and changes PrePivot by the same amount in one tick,
  so `SimPawnView` interpolates PrePivot with the location between ticks. The drawn pose matches the original's bone
  for bone (Bones against the original, below), so the feet go where the original's go: in the steady stances the
  toe joints are 2.5 uu under the cylinder's bottom (crouched: PrePivot.Z 37.5, height 50; standing: 0 and 87.5;
  the original reads the same), and in part blends they go lower in both: the original's toes reach 12.8 uu under
  the floor while a crouched walk blends in (JoggCrAlFd0 at 0.6) and 14.5 uu while it stands up out of a walk
  (WaitCrAlEd0 under the fading JoggStAlFd0 and JoggCrAlFd0); ours reach 8.8 (WaitCrAlEd0 under fading walk
  channels) and 15.3 (play-crouch). Ryan's "up to 13 uu" for the player is the original's own blend. Bots: the
  play-inspect readings of up to 36 uu are proxies a tick behind their bot where the floor steps (the floor trace
  is taken under the new location while the mesh is drawn at the old one); placement is otherwise the server's.
- Night vision drew pawns near black: the night post pass (game 0x10963090) reads the scene before the glow
  composite, so it has no dark gate, and the boost byte (30) reaches every lit surface as 2 x 15 / 255 on the light
  term, pawns included (docs/RENDERING.md, vision modes). The spy at the spawn now reads 4 / 7 / 31 (10th, 50th,
  90th percentile of green) as in the game's capture, before 0 / 0 / 37.

## Bones against the original (anim-bones smoke)

Captures go to `local/difftest/golden-bones/spy-*.jsonl`
(game-derived, never tracked). Each line is one frame of the sandbox's Play Level game (SteeE, the spy): the pawn
fields, PrePivot, channels 0 to 15 (sequence, frame, rate, alpha, tween) and the mesh instance's space bases, the
TArray at MeshInstance+0xB4 (43 bones of 48 bytes: origin and three axes, mesh space, Z up; the same array is at
+0x178). `--svm-smoke=anim-bones` feeds each frame's channels to `ChannelPoseApplier` on SPerso.ATT_01 (channels 3,
6, 7, 11 and 12 backward, as the walk blender's first call plays them on the spy) and compares all 43 bone origins
(Godot X, Y, Z x 100 is UE mesh X, Z, Y). With the two captures (crouched idle, stand-up and crouch clips, the
turn in place with channel 3 backward, crouched taps, walks and stops, stand-up out of a walk, crouch-down while
walking) 663 of 666 and 780 of 786 frames match within 1.5 uu on every bone; the few others are frames where the
read straddles the next pose build while a foot moves fast (worst 11.4 uu). It skips when no capture is there.

## Third-person camera next to walls

`tests/OpenScctGame.Logic.Tests/Views/WallCameraTests.cs` puts the spy at the SteeE spawn as the game captures do
(`tools/golden/poses/SteeE-walls.json`; frames and cameras under `local/golden/game-wall/`, from
`tools/golden/capture_game.py`) and compares the client's camera with the game's vCamLocation: all twelve poses match
to 0.1 uu, open views and walls alike. With a wall right behind the spy the game pulls the camera in to
m_absoluteMinDist (30 uu, inside the spy's cylinder) and its frame is filled by the spy's back; with the crouched spy
behind a low wall the game's camera loses sight of him too, so the inspector flags only a camera nearer than 22 uu.

Two solver details of editor 0x111f56a0 were off (spec/native/camera.md rules 12 and 13): the collision sweep's hit
distance is reached from the last frame's distance (m_prevDistance), not from step 10's distance, which had already
moved out by interSpeed dt and so cancelled the move in (with collInterSpeed equal to interSpeed the camera never came
in past the final 12 uu clearance and stood 6 to 11 uu out); and the second final clearance hit is held at
m_absoluteMinDist like the first (the camera came to 24 uu instead of 30). The TRACE_Camera reading in OpenScct.Physics
was not the cause: ignoring it changes no pose.

## Clip timing against the original

Measured in the original (the sandbox's Play Level game, the channels of the local pawn and of its weapon read each
frame at 60 fps) and in the play client (`tests/OpenScctGame.Logic.Tests/Views/AnimTimingTests.cs`, the inline play
session; clip fraction a second, loop time in brackets):

| Clip | Original | Client | Note |
|---|---|---|---|
| merc c0 WaitStSpFd2 (8 frames at 8 fps) | 1.000 (1.0 s) | 1.000 | also the movement goldens: 0.01665 a 1/60 s sample |
| spy c0 waitCrAlFd0 (9 at 8) | 0.889 (1.125 s) | 0.882 | the 1/9 wrap step is lost in the measure |
| merc first-person FirstWaitStFd2 (15 at 15) | 1.000 (1.0 s) | 1.000 | |
| merc first-person FirstWalkStFd2 (11 at 15) at 346.5 uu/s | play rate 2.079 (0.35 s) | 2.056 at 342.7 uu/s | before: play rate 1 (0.73 s) |
| merc bot proxies' turn-in-place (WalkStSpRt2 on c2, c3) while standing | - | as much clip as the bot's own on the server | fixed: see below |

The idle loops already ran at the original's rate. What looked like a fast, shuffling merc idle was the bots'
turn on the clients: a standing bot's proxy turned 1.90 times as far as the bot on the server (MercBot1: 357468
against 188575 yaw units standing in 40 s) because the Rotation our server pushes between position samples
(net-proxy.md rule 3) turned the proxy at once, and the next proxy tick turned it back toward the last sample's yaw
(rule 15), back and forth. In the original `ASBasePawn` overrides PostNetReceive (editor 0x1120be70, rule 15a): the
received Rotation never turns a pawn standing on the world; it becomes the proxy's ReceivedRotation, the target the
proxy tick turns toward. Ported in `PawnProxyReceiver` (`NetDriver.PreNetReceive` and `PostNetReceive` hooks): the
proxies now turn 0.48 to 0.74 times their bots' standing yaw travel (each push only moves the target, and the
server's turns inside a 0.09 s push window never reach the client), and their turn clips play about as much as the
bots' own (MercBot1 3.5 clip lengths against 4.6). `AnimTimingTests.StandingProxiesTurnNoFurtherThanTheirPawns`
checks both. Not compared with the game: a capture needs a second client, and the sandbox runs one game. The bots
themselves turn a lot while standing (about three turns in 40 s); that is the bot code, not the animation.
