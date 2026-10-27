# Skeletal animation natives: channels, PlayAnim, tweens, blending, AnimEnd, notifies, root motion

Units:
- nat:Engine.Actor.PlayAnim
- nat:Engine.Actor.LoopAnim
- nat:Engine.Actor.TweenAnim
- nat:Engine.Actor.PlayAnimAfter
- nat:Engine.Actor.SetAnimFrame
- nat:Engine.Actor.AnimBlendParams
- nat:Engine.Actor.AnimBlendToAlpha
- nat:Engine.Actor.LockRootMotion
- nat:Engine.Actor.GetRootLocationDelta
- nat:Engine.Actor.GetRootRotationDelta
- nat:Engine.Actor.EnableChannelNotify
- eng:USkeletalMeshInstance::UpdateAnimation
- eng:ASBasePawn::UpdateMovementAnimation

## Purpose

The animation layer under ExecuteAction (execute-action.md) and the physics root-motion mode
(physics-rootmotion-repositioning.md): how a clip is started on a channel, how frames advance, when AnimEnd and
anim notifies fire, how channels blend into the pose, how the walk blender drives channels 1 to 13, and how root
motion is locked and measured. The simulation needs the timing parts (frame advance, AnimEnd, notifies, root
motion); the pose parts matter for the client view and for TraceBone (hit-zones.md), which samples the pose.

## Data

### Channel record (one per channel, 0x90 bytes, array on the mesh instance)

| Offset | Field | Use |
|---|---|---|
| +0x04 | anim set index | which linked animation object holds the sequence (-1 none) |
| +0x08 | AnimSequence | FName of the clip |
| +0x0c | AnimRate | normalised frames per second (1.0 = the whole clip per second); negative = scaled by the actor's speed |
| +0x10 | AnimFrame | 0 to AnimLast; negative while tweening in |
| +0x14 | AnimLast | 1 - 1 / NumFrames (the last frame start) |
| +0x18 | TweenRate | rate of the tween in, normalised |
| +0x1c | OldAnimRate | the rate before the last PlayAnim (used by tween -1) |
| +0x20 | base rate | clip rate / NumFrames (AnimRate = Rate x this) |
| +0x2c | finished flag | cleared by PlayAnim |
| +0x30 | bAnimLoop | |
| +0x34 | bAnimNotify | the clip has notifies |
| +0x38 | bTimedBlend | AnimBlendToAlpha in progress |
| +0x44 | bAnimBackward | play from the end |
| +0x48 | bNotifyIgnore | EnableChannelNotify(false): no notifies and no AnimEnd |
| +0x4c | bGlobalPose | AnimBlendParams bGlobalPose |
| +0x50 | BlendAlpha | 0 to 1 (channel 0 is always 1) |
| +0x54, +0x58 | BlendInTime, BlendOutTime | stored, at most 1 |
| +0x5c, +0x60 | blend timer, target alpha | AnimBlendToAlpha |
| +0x64, +0x6c, +0x70 | tween-from cache | pose blend from the previous clip during a tween |
| +0x68 | StartBone | bone index from AnimBlendParams (0 = root) |
| +0x74 | bMirror | the extra PlayAnim flag: the pose is mirrored through Pawn.BoneMirrors |
| +0x78 to +0x8c | queued clip | PlayAnimAfter: sequence, rate, time, loop, backward, valid |

Channels: up to 257 (index 0 to 256); a channel record is created on first use. The pawn uses 25 (ExecuteAction
and the walk blender):

| Channel | Use |
|---|---|
| 0 | base full-body clip (actions, wait loops); drives root motion |
| 1 | upper-body jog overlay (bNoRunAnimation landing, PlayLanding 96) |
| 2, 3 | turn in place (TurnAnim, forward and backward) |
| 4 | run cycle (RunAnim); its frame is the run cycle phase (left foot in 0.1 to 0.6) |
| 5, 6 | forward and backward walk (ForwardAnim) |
| 7, 8 | left and right strafe (RightAnim, or LeftAnim for the merc) |
| 9 to 13 | copies of 4 to 8 holding the previous clips while a set changes |
| 14 | weapon draw, holster and fire transitions (@B Spine1) |
| 15 | roll, landing overlay and the blend-with-channel-0 clips |
| 16 | reanimate |
| 17 | damage reactions (@B Head) |
| 18 | goggles head movement (@B Head) |
| 19 | melee attacks, drop-attack landing (@B Spine1) |
| 20 | cough (@B Head) |
| 21 | reload and fire (@B Spine1) |
| 22 | palm, binoculars, quick throw (@B Spine1) |
| 23 | electric damage |
| 24 | hand interact, mines, switches (@B Spine1) |

## Rules

### 1. PlayAnim (AActor::PlayAnim 0x11189250, USkeletalMeshInstance::PlayAnim 0x110ff8f0)

Native argument order: (Channel, Sequence, Rate, TweenTime, bLoop, bBackward, bContinueAtFrame, bMirror). The
script PlayAnim and LoopAnim pass bLoop 0 and 1 and bMirror 0; only native callers (ExecuteAction) mirror.

1. When the actor replicates animations (bReplicateAnimations), the SimAnim loop bit is set from bLoop and the
   actor is marked dirty.
2. The channel must exist (grown on demand); the sequence must be found in the linked animations; otherwise a
   warning is logged and nothing plays.
3. bMirror is stored on the channel.
4. Interrupted clip notifies: when the channel was playing a non-looping clip with notifies and the new sequence
   differs, every script notify (AnimNotify_Script) of the old clip that lies after the current frame (before it,
   when backward) fires now, in order, with the notify channel set to this channel. Other notify types are
   dropped.
5. Looping play (bLoop): N = the clip's frame count. If the channel already loops this sequence and is
   animating, only the rate changes (AnimRate = Rate x base rate, OldAnimRate the same, backward flag stored) and
   the frame continues. Otherwise: sequence set, AnimRate = Rate x base rate, AnimLast = 1 - 1/N, loop on,
   backward flag stored, notify flag from the clip; then the tween rules of step 7.
6. Non-looping play: Rate 0 is a pose (TweenAnim): AnimLast = 0, AnimRate = 0, the tween rules apply with the
   frame target 0; a negative Rate is refused. A positive Rate: as step 5 with loop off. With bContinueAtFrame and
   a changed backward flag, the frame is mirrored in time (AnimLast - frame).
7. Tween (all cases, after the above): a one-frame clip (AnimLast 0) tweens over TweenTime, or at a fixed rate of
   10 per second without a tween, with AnimRate 0. TweenTime > 0: TweenRate = 1 / (N x TweenTime) and the frame
   starts at -1/N (the tween runs for TweenTime seconds, blending from the previous pose). TweenTime = -1: tween at
   the previous clip's rate (OldAnimRate), or for a moving clip from a pose a rate derived from the actor's speed,
   or 1 / (0.025 N). TweenTime 0 (or below, other than -1): no tween; the frame restarts at 0 (1e-4 for a
   non-looping clip) unless bContinueAtFrame.
8. TweenAnim(Sequence, Time, Channel) = PlayAnim(Channel, Sequence, rate 0, tween Time).

### 2. Frame advance, AnimEnd and notifies (UpdateAnimation, editor 0x111056c0, once per actor tick)

For each channel in index order, with the frame time dt:

1. Timed blend: BlendAlpha += (target - alpha) x min(1, dt / timer); timer -= dt (not below 0); when it reaches 0
   the timed blend ends and, if the target was 0, the channel's AnimRate is set to 0 (the channel stops).
2. Up to 4 passes while time is left and the channel is animating:
   - Tweening (frame < 0): frame += dt x TweenRate. Still below 0: done. Else frame = 0, the leftover time carries
     on; a pure tween (AnimRate 0) sets the notify channel and raises AnimEnd (unless bNotifyIgnore).
   - Playing: a frame of exactly 0 is nudged to 1e-4; else frame += dt x AnimRate, where a negative AnimRate means
     |AnimRate| x |actor Velocity| (at least 0.3 per second) - the speed-scaled loop.
   - Notifies: when the clip has notifies and the channel does not ignore them, the earliest notify with a time in
     (old frame, new frame] (mirrored times when backward) that has not fired this tick fires: the frame stops at
     the notify time, the leftover time is kept for the next pass, the notify channel is set, and the notify's
     Notify(owner) runs. Several notifies in one tick fire in time order, one per pass.
   - End of clip (frame >= AnimLast): in root motion (owner Physics 5) the first time the frame crosses AnimLast it
     is clamped to AnimLast with no time left (so the last root delta is taken this tick); the end logic runs on
     the next tick. Otherwise a non-looping clip holds at AnimLast, AnimRate = 0, and:
     - a queued PlayAnimAfter clip starts (its sequence, rate, tween time, loop, backward); a queued None sequence
       instead fades the channel out (AnimBlendToAlpha(0, time) when time > 0, else alpha 0 at once);
     - AnimEnd(channel) is raised on the actor and then on its Controller, unless bNotifyIgnore or the rate was not
       positive.
     A looping clip wraps (frame -= 1, the leftover time carried) and raises AnimEnd each loop (same conditions).
3. After channel 0: the actor's SimAnim (bReplicateAnimations actors) is updated from channel 0: sequence, rate
   (as (rate + 4) x 31 in a byte, rate clamped to [-4, 4]; a fixed 124 under a condition not decoded), loop bit,
   tween rate and frame bytes; a change of sequence, rate byte or loop bit marks the actor dirty.

### 3. Blending (AnimBlendParams, AnimBlendToAlpha, pose build)

1. AnimBlendParams(Stage, BlendAlpha, InTime, OutTime, BoneName, bGlobalPose): the channel is created on demand;
   channel 0 logs a warning but is still written in the editor build, while the game's instance helper (game
   0x109b6de0) refuses channel 0 and writes nothing; StartBone = the bone named BoneName (0, the root and so the
   whole body, when not found);
   BlendAlpha stored as given; InTime and OutTime stored, values above 1 become 1; the timed blend is cancelled.
2. AnimBlendToAlpha(Stage, TargetAlpha, TimeInterval): target and timer stored, timed blend on (rule 2.1).
3. Pose build (editor 0x11109a50): channels are applied in index order. Channel 0 has weight 1; any other channel
   contributes only when it has a clip and BlendAlpha > 0. A channel affects its StartBone and every bone below it
   in the hierarchy (bones are ordered parent first; a bone is included when its parent is). Each included bone
   gets the channel's sampled rotation (quaternion) and translation, blended over the accumulated pose by
   BlendAlpha (quaternion slerp and linear translation). bGlobalPose makes the start bone use the channel's
   global (mesh-space) transform. A tweening channel blends from its cached previous pose by 1 - frame / start
   frame. A mirrored channel swaps bones through Pawn.BoneMirrors and reflects their transforms.
4. When the mesh instance has a root lock (rule 5), the root bone's translation in the rendered pose is reduced by
   the root's motion since the lock, so the motion the actor carries is not drawn twice: drawn root = root bone(t) +
   R(lock) - R(t), with R the root sample the root update uses (the clip's root track when it has one, else the
   root bone; rule 5.2) and R(lock) stored by LockRootMotion. The root is not pinned: for a clip without a root
   track this is the root bone at the lock (pinned in effect); for a clip with one the body shows the bone's own
   movement around the moving actor (the boost's run-up into the helper's hands, coop.md rules 24 and 25). R is
   absolute, so a clip change while locked (the wall jump's second clip) still measures from the lock. A backward
   play also uses R(0), the root at the start of the backward play (the clip's end key): bone + 2 R(lock) - R(0) -
   R(t).
5. With lock bit 2 the root is also turned back about the vertical by the root's yaw since the lock, position and
   rotation together. Forward: yaw of R(t) minus yaw of R(lock), negated for a bone clip that is mirrored and for a
   track clip that is not (the sign rules of rule 5.4), which is minus the root turn GetRootRotationDelta gave the
   actor since the lock; other turns of the actor (a replicated correction, a turn before the lock) are not taken
   out. Backward: yaw of R(t) minus yaw of R(0) with no sign rule. The actor's yaw carries that turn, so the body
   stays where the move put it. The forward position turned is bone +
   R(lock) - R(t); a backward play with bit 2 turns bone - R(t) only (no R(lock) or R(0) term). Bit 4 then sets the
   vertical part of the turned position to the bit-2-free value (bone + R(lock) - R(t) forward, bone + 2 R(lock) -
   R(0) - R(t) backward). The turn is about the vertical, so bit 4 changes nothing in a forward play; it only puts
   R(lock) and R(0) back into the height of a backward bit 2 play. No ExecuteAction case plays a root-motion clip
   backward, so the backward forms are not reached in play.

### 4. Other channel natives

- SetAnimFrame(Time, Channel, UnitFlag): frame = Time, or Time / (NumFrames - 1) with UnitFlag (frames given in
  frame units).
- PlayAnimAfter(Sequence, Rate, Time, Channel, bBackward): queues the clip on the channel (step 2.2 end of clip).
- EnableChannelNotify(Channel, bEnable): bNotifyIgnore = not bEnable.
- FinishAnim (latent) waits until the channel stops animating.
- Channel rate scale (native helper, editor 0x11102730): AnimRate = scale x base rate; scale 0 freezes the
  channel (ExecuteAction's `rate0`).
- Step blend (editor 0x111029a0): moves BlendAlpha toward a target by at most a step (the walk blender's tool;
  1e9 = at once).
- SetAnimSequence (editor 0x11102a10): changes a channel's clip without restarting it (frame and rate kept).
- CopyAnimation (editor 0x11102620): copies a channel record to another channel (the walk blender keeps the
  outgoing clip on channels 9 to 13).

### 5. Root motion (LockRootMotion, GetRootLocationDelta, GetRootRotationDelta)

1. LockRootMotion(Lock) (instance 0x11105e70): stores Lock, builds the lock frame from the actor's Location
   (+PrePivot unless the instance flag at +0x74 is set), Rotation, DrawScale x DrawScale3D x the mesh scale and
   the mesh origin; when Lock is non-zero and the actor is a pawn, vLocationBeforeRM = Location and rRotBeforeRM =
   Rotation.Yaw. The root of channel 0 is then evaluated at the current frame (and, with Lock, at frame 0) and
   both the last root location and rotation are reset to it, so the first delta is zero. R(lock) and R(0) are
   taken only here (game 0x109b82b0): a clip change while locked keeps the first clip's R(lock) and R(0), and the
   last root rotation is not reset either, so GetRootRotationDelta's first delta on the new clip is the new clip's
   root yaw minus the old clip's last one (rule 5.4).
2. The root update (editor 0x111015f0, game 0x109b9c40) evaluates channel 0 only: its clip at the current frame
   (frame clamped to [0, 1], backward and loop aware), mirrored when channel 0 is mirrored, expressed in the lock
   frame; it stores the root location and rotation. The sample is the root track sampler's (position and rotation)
   when the sequence has track keys, else the root bone's. The sample frame is f x N forward, (1 - f) x N for a
   looping backward play and (1 - f) x N - 1 (not below 0) for a one-shot backward play, with f the channel frame
   clamped to [0, 1] and N the clip's frame count. When the lock is 0 and channel 0 reached AnimLast, the actor's EndRootMotion
   is called.
3. GetRootLocationDelta: root location now minus at the last call (then the last value is updated); for a
   backward clip the delta is turned by the root yaw difference.
4. GetRootRotationDelta (game 0x109b7f30): (0, yaw, 0) with yaw = previous root yaw - current root yaw, negated when channel 0 is
   mirrored and negated again when the sequence has root track keys (+0x48 of the sequence record, the track key
   count the root update tests) or the clip data is missing. The root sample of a clip with a root track is the
   track's (rule 5.2), so such a clip turns the pawn by the track's rotation keys, which run the other way from the
   bone's; a track with one rotation key does not turn the pawn whatever the root bone does (the co-op hoists,
   GrabLgEdRailW, the ladder exits), and a track with one position key holds that position (LedgStAlTiL and the
   Xxxx*2 poses: no root translation from the bone).
5. physRootMotion (physics-rootmotion-repositioning.md) locks with iLockRootMotionType on first use, runs the root
   update, takes the location delta as vDeltaMove and applies the rotation delta only with lock bit 2.
6. Lock values used by ExecuteAction: 1 (translation), 3 (translation and yaw), 5, 7. In the rendered pose any
   lock above 0 offsets the root's translation by R(lock) - R(t) (rule 3.4), bit 2 also turns the root back by its
   yaw since the lock, and bit 4 recomputes the vertical part of the turned root without the turn (rule 3.5; a
   change only in a backward play).
7. On a simulated proxy (Role 2) the walk blender re-locks when the replicated Physics enters or leaves
   RootMotion (rule 6.2): lock iLockRootMotionType on entry, 0 on exit.

### 6. Walk blender (ASBasePawn::UpdateMovementAnimation, editor 0x112008a0, every tick)

1. Relative velocity V = Velocity - Base.Velocity; the script event TurnHead is called first (head look-at).
2. Dead (Health < 1) or bLyingOnGround: channels 1 to 24 step toward alpha 0 at 10 x dt per tick and are frozen;
   stop. During a blend-with-channel-0 (fBlendTimeWithC0 > 0): ChannelForBlendWithC0 steps to alpha 1 at
   dt / fBlendTimeWithC0 and channels 4 to 13 fade the same way; stop. Otherwise:
3. When channel 0 has no clip, c0 AWaitSt0 loop t0.1 is played. On the first call (bInitializeAnimation): channels
   2 to 13 get AWaitSt0 loops (3, 6, 11 and, for the spy, 7 and 12 backward), notifies off on 2 to 13 except 5.
4. Step size per tick: dt / BlendChangeTime (BlendChangeTime is set by ExecuteAction: 0.25 by default, 0.1 for a
   forward jump, 0.15 on a ledge, 0.05 on a jump start, 20 in the BTW corner).
5. By state: Vandamme with IK 5 and the horizontal-pipe fire states drive aim blends from iYawPitchFromView
   (yaw error x 8.33e-5 and pitch x 1.097e-4, clamped to [-1, 1]; with PipH2 the yaw scale is 6.13e-5 around a
   half turn and pitch x 1e-4); BTW, ledge shimmy (bBeginSpecial12Anim in Ledge), FollowParent and pipe or ladder
   holds run their own blends; RootMotion, Linear, OnPipe, OnLadder and Vandamme fade channels 2 to 13 at once.
6. Within 2 s after a feet ledge: c4 alpha 1, channels 5 to 13 to 0 at once, and c5's frame 0 (left foot) or 0.5.
7. Falling, or before MovementBlendStartTime: channels 2 to 13 fade; a roll in Falling fades c15 once its frame
   passes 0.65 and clears bIsRolling.
8. Standing still (|V.xy|^2 < 2, or Ledge): channels 4 to 13 fade; in Walking the turn-in-place channels 2 and 3
   play TurnAnim at alpha 0.5 standing or 0.75 crouched with a rate scale of |turn rate| x 4.17e-5 when the yaw
   turns faster than 5000 per second (2 for one direction, 3 for the other); the turn rate is smoothed for remote
   pawns over 0.3 s (simulated) or 0.1 s.
9. Moving: forward speed f and side speed s from V in the pawn's frame. Rate R = (|f| + |s|) / fBlendCoeffWalkAnim;
   side weight w = 1 / (|f / s| + 1) when |s| > 1. When walking slowly (R < 1, not crouched, run limit above walk
   limit, not the charge set 385), the channels play at rate 1 and their alpha is scaled by R. Between
   fWalkSpeedLimit and fRunSpeedLimit the run channel 4 fades in (fraction of |f| between the limits) and R mixes
   the walk and run coefficients. Forward (c5) or backward (c6) takes alpha (1 - w) x scale, strafe right (c7) or
   left (c8) takes w x scale; channels 9 to 13 fade. A set change (ForwardAnim, RightAnim, LeftAnim, RunAnim)
   copies the outgoing clip to the copy channel (9 to 13) with its alpha, then switches the main channel's clip.
   c5's rate scale is R; channels 4 and 6 to 13 take c5's frame each tick (all walk channels stay in phase).

## States

None of its own; the channel records and the walk blender's fields (fBlendTimeWithC0, OldRotYaw,
fDistantTurnRate, fOldFrame) persist on the actor and its mesh instance.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Max channel index | 256 | - | decoded | editor 0x110fec90 |
| Frame nudge at start | 1e-4 | normalised | decoded | 0x110ff8f0, 0x111056c0 |
| Passes per channel per tick | 4 | - | decoded | 0x111056c0 |
| Speed-scaled rate floor | 0.3 | 1/s | decoded | 0x111056c0 |
| One-frame tween rate without tween time | 10 | 1/s | decoded | 0x110ff8f0 |
| Tween -1 fallback | 1 / (0.025 N) | 1/s | decoded | 0x110ff8f0 |
| SimAnim rate byte | (clamp(rate, -4, 4) + 4) x 31 | - | decoded | editor 0x110a6d10 |
| Blend times stored at most | 1 | s | decoded | 0x111005a0 |
| Turn-in-place threshold | 5000 | yaw units / s | decoded | 0x112008a0 |
| Turn-in-place alpha | 0.5 standing, 0.75 crouched | - | decoded | 0x112008a0 |
| Turn rate scale | 4.1666666e-5 | per yaw unit / s | decoded | 0x112008a0 |
| Dead or lying fade | 10 x dt per tick | alpha | decoded | 0x112008a0 |
| Feet ledge window | 2 | s | decoded | 0x112008a0 |
| Roll c15 fade start | frame 0.65 | normalised | decoded | 0x112008a0 |
| Slow walk rate floor | 1.0 | - | decoded | 0x112008a0 (constant 0x11626c4c) |

## Network

Animation runs on every machine from the actions each machine executes (execute-action.md Network). Actors with
bReplicateAnimations replicate channel 0 through SimAnim (rule 2.3); SBasePawn relies on actions instead. Root
motion is measured locally from channel 0 on each machine; a simulated proxy re-locks when its replicated
Physics enters or leaves RootMotion (rule 5.7).

## Edge cases

- In root motion a non-looping clip ends one tick later than in other modes (rule 2.2), and its AnimEnd fires on
  that later tick.
- A notify on the exact start frame of a clip can fire because the start frame is nudged to 1e-4 only when the
  frame is exactly 0 at the advance.
- Interrupting a non-looping clip fires its remaining script notifies at once (rule 1.4); a port that drops them
  loses script side effects (sounds, gadget triggers).
- PlayAnim on channel 0 does not reset the channel's blend; channel 0 is always at weight 1.

## Evidence

- Editor: AActor::PlayAnim 0x11189250, USkeletalMeshInstance::PlayAnim 0x110ff8f0, PlayAnimAfter 0x11100210,
  SetBlendParams 0x111005a0, BlendToAlpha 0x11102b50, SetAnimFrame 0x11102870, SetAnimSequence 0x11102a10,
  CopyAnimation 0x11102620, EnableChannelNotify 0x11101270, IsAnimating 0x110fed00, UpdateAnimation 0x111056c0
  (instance vtable 0x1149da50 slot +0x9c), pose build 0x11109a50, LockRootMotion 0x11105e70, root update
  0x111015f0, GetRootLocationDelta 0x11100d70, GetRootRotationDelta 0x11100ef0, SimAnim update 0x110a6d10, AnimEnd
  dispatch 0x11208a60 (actor vtable +0xc8), UpdateMovementAnimation 0x112008a0, ZeroMovementAlpha 0x110dd700.
- Exec thunks: PlayAnim 0x11185cd0, LoopAnim 0x11185e60, TweenAnim 0x11185f30, FinishAnim 0x11185a70,
  PlayAnimAfter 0x11185da0, StopAnimating 0x11189450, SetAnimFrame 0x11186070, AnimBlendParams 0x111866c0,
  AnimBlendToAlpha 0x111867c0, GetRootLocationDelta 0x11186570, GetRootRotationDelta 0x111865e0, LockRootMotion
  0x11186650.
- Game: PlayAnim 0x10a23590 (exec 0x10a23610), same argument order. LockRootMotion 0x109b82b0 (lock at instance
  +0x184, R(lock) copied from +0x250 to +0x1f0, frame-0 sample at +0x2b0), root update 0x109b9c40 (track keys at
  sequence +0x48 choose the track sampler, vtable +8, over the bone sampler, vtable +4), GetRootRotationDelta
  0x109b7f30, pose build 0x109ba4f0 (instance vtable 0x10c0a048 slot +0x108; the bone 0 lock block from
  0x109bc7c9: bit 2 at 0x109bc839 and 0x109bcbac, bit 4 at 0x109bcb88 and 0x109bcf0e). Game instance fields are the
  editor's minus 0x34 (editor +0x224, +0x284, +0x2e4 are game +0x1f0, +0x250, +0x2b0).

## Open questions

1. The exact bone math of the mirror (which axis is reflected) and of bGlobalPose; they only matter for the
   rendered pose and the hit pills of mirrored clips.
2. Resolved: +0x48 of the sequence record is the root track key count (rule 5.4).
3. The mirrored pose build (bone swap through Pawn.BoneMirrors at pawn +0x448, the global-space pass in game
   0x109ba4f0) is not decoded rule by rule.
4. Port gap: the converted root yaw of each clip is relative to its first key, so OpenSCCT starts a clip that
   follows another one while locked from that clip's own first key (no turn at the change), where the original
   turns by the new clip's first root yaw minus the old clip's last (rule 5.1). The shipped wall-jump pairs
   (WljpStAlBg into WljpStAlBk) are continuous at the change, bone and track, so no case in play is known to
   differ.
