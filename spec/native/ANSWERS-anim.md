# Answers from the animation study (channel timing, walk blender, pose order)

Rules decoded while porting the animation channels end to end (sim channel model, walk blender, client pose) and
checked against the per-frame channel fields of the real-game goldens (tests/OpenScct.DiffTest/Anim). They complete
anim.md; spec owners fold them in. The port (src/OpenScct.Game/Movement/PawnAnimation.cs, src/OpenScct.Game/Animation/)
follows them already.

## Spec corrections

| # | Spec | Says | Should say | Evidence |
|---|---|---|---|---|
| 1 | anim.md rule 2.2, looping clip | wraps with frame -= 1 and raises AnimEnd each loop | a loop raises AnimEnd when its frame first crosses AnimLast (not at the wrap); when the frame reaches 1 it is set to 0 (not frame - 1) and the time left goes to the next pass, which only nudges the 0 to 1e-4 and ends the channel's update, so that time is lost | UpdateAnimation 0x111056c0; goldens: every loop wrap shows 1e-4 |
| 2 | anim.md rule 3.1, AnimBlendParams on channel 0 | logs a warning but is still written | channel 0 is not written at all (warning only); channel 0 keeps BlendAlpha 0 and is drawn at weight 1 | SetBlendParams 0x111005a0; goldens: 'ach' channel 0 alpha is always 0 |
| 3 | anim.md rule 2.2, bAnimBackward | (implied) the frame runs backward | the frame always rises from 0 to AnimLast; only the notify window and the sampled key are mirrored (rule A5) | UpdateAnimation 0x111056c0, pose build 0x11109a50 |
| 4 | anim.md rule 4, SetAnimSequence | changes a clip without restarting it (frame and rate kept) | frame and AnimRate are kept, but the base rate (clip rate / N) and the notify flag are rewritten, so the next rate scale uses the new clip; loop, backward and the anim set index are untouched | 0x11102a10 |
| 5 | anim.md rule 6.9, slow walk | the channels play at rate 1 and their alpha is scaled by R | as written, plus: outside the slow walk the forward channel's target alpha is 1 whatever the strafe weight (the strafe channel adds w on top) | 0x112008a0 |
| 7 | anim.md rule 6.5, pipe fire aim | with PipH2 the yaw scale is 6.13e-5 around a half turn and pitch x 1e-4 | that is PipH4 (PipHFire 2); PipH2 (3) uses the Vandamme scales (8.33e-5, 1.097e-4) without the pitch offset | 0x112008a0 |
| 8 | anim.md rule 6.1, TurnHead | the head look-at | the script event also packs the controller's and the desired rotation into iYawPitchFromView and iDesiredYawPitch (the aim blends read them) and keeps IK_Type_Previous | SBasePawn.TurnHead |
| 6 | ANSWERS-fidelity.md, the climb step | channel 5 plays ForwardAnim, channel 6 mirrors it | channel 5 (up) or 6 (down) takes alpha 1 at once and the other 0; entering the middle half of the step flips channel 0's mirror flag off, leaving it flips it on (the hold loop follows the leading hand) | 0x11200140 |

## A1. PlayAnim (USkeletalMeshInstance::PlayAnim 0x110ff8f0), complete

N is the clip's frame count, base = clip rate / N.

1. A looping play of the sequence the channel already loops, while the channel animates, only sets AnimRate =
   OldAnimRate = Rate x base, the backward flag and clears the finished flag. Nothing else changes.
2. Otherwise the sequence, base, AnimRate = Rate x base and AnimLast = 1 - 1/N are set, the finished flag cleared, the
   loop flag set from bLoop, the backward flag stored, and the notify flag set when the clip has notifies.
3. A one-frame clip (AnimLast 0): no notifies, OldAnimRate 0, AnimRate 0, frame -1/N, TweenRate 1 / TweenTime when
   TweenTime > 0, else 10. A pure tween to that pose.
4. TweenTime > 0: TweenRate = 1 / (N x TweenTime); without bContinueAtFrame the frame starts at -1/N; OldAnimRate =
   AnimRate. With bContinueAtFrame a one-shot whose backward flag changed mirrors its frame (AnimLast - frame).
5. TweenTime = -1: frame -1/N (unless bContinueAtFrame); TweenRate = the old OldAnimRate when it is positive; when
   it is negative (a speed-scaled clip) TweenRate = max(AnimRate / 2, |Velocity| x -OldAnimRate); else
   1 / (0.025 N). OldAnimRate = AnimRate.
6. Any other TweenTime: TweenRate 0, and without bContinueAtFrame the frame is 1e-4 for a loop and exactly 0 for a
   one-shot. OldAnimRate = AnimRate.
7. A one-shot on a channel that holds no sequence plays without tween (TweenTime forced to 0).
8. Rate 0 without loop is TweenAnim: AnimLast 0, AnimRate 0, OldAnimRate 0, no notifies, loop off, frame -1/N with
   TweenRate 1 / (N x TweenTime) when TweenTime > 0, else frame 0 and TweenRate 0. A negative rate without loop is
   refused.
9. The anim set index of the channel is set on every accepted play; a channel only advances once it has one (A2).

## A2. UpdateAnimation (0x111056c0), complete

For each channel in index order, dt = the tick time:

1. Timed blend (anim set index valid, dt > 0): f = min(1, dt / timer); alpha += (target - alpha) f; timer -= dt
   (floor 0); at 0 the timed blend ends and a target of 0 also sets AnimRate 0.
2. The fired-notify flags of the channel are cleared. Then passes, at most 4, while the channel has an anim set,
   animates (it has a sequence and, by the sign of its frame, TweenRate or AnimRate is non-zero) and time is left.
   old = frame at the start of the pass.
3. Frame below 0 (tween): frame += dt x TweenRate. Still below 0: the channel is done. Else frame = 0, the time left
   is dt x new / (new - old); with AnimRate 0 (a pure tween) the AnimEnd channel is set and, unless bNotifyIgnore,
   AnimEnd is raised; the next pass follows (and nudges the 0).
4. Frame exactly 0: frame = 1e-4. This uses the pass without advancing.
5. Otherwise frame += dt x AnimRate, or for a negative AnimRate dt x max(0.3, |Velocity| x -AnimRate).
6. Notifies (the clip has notifies, bNotifyIgnore off): the not yet fired notify with the smallest time t in
   (old, frame] (backward: AnimLast - t in (old + 1e-6, frame]) fires: the time left becomes
   dt x (frame - t) / (frame - old), the frame stops at t (backward: AnimLast - t), the notify channel is set and the
   notify object's Notify(owner) runs; the next pass follows.
7. Frame below AnimLast: the channel is done (the remaining time, if any, is dropped).
8. Frame at or past AnimLast:
   - owner in PHYS_RootMotion and old < AnimLast: frame = AnimLast, no time left (the end logic runs next tick);
   - one-shot: frame = AnimLast, the AnimEnd channel is set, time left dt x (frame - AnimLast) / (frame - old); a
     queued PlayAnimAfter clip starts (PlayAnim with its rate, tween, loop and backward, then the queue is cleared),
     or for a queued sequence that does not resolve the channel fades (BlendToAlpha(0, time) when time > 0, else alpha
     0 at once). Then when bNotifyIgnore is set or AnimRate is not positive, AnimRate = 0 and no AnimEnd; else
     AnimRate = 0 and AnimEnd is raised (so a queued clip that just started is frozen; no SBase caller queues);
   - loop: at frame >= 1 the frame becomes 0 with time left dt x (frame - 1) / (frame - old), else no time left; when
     old was below AnimLast the AnimEnd channel is set and, unless bNotifyIgnore, AnimEnd is raised.
9. AnimEnd goes to the actor's script AnimEnd(channel), then to its Controller's (ASBasePawn::NotifyAnimEnd
   0x11208a60).
10. After channel 0 the actor's SimAnim is refreshed (bReplicateAnimations actors only; pawns replicate actions).

## A3. Channel helpers used by the walk blender and ExecuteAction

| Helper | Editor | Effect |
|---|---|---|
| GetAnimFrame, GetBlendAlpha, GetSequence | 0x11102970, 0x11102840, 0x11102b10 | read frame (+0x10), alpha (+0x50), sequence (+0x08); 0 / None for a missing channel |
| ClipRate(ch) | 0x11105660 | the channel's clip rate in frames per second, 30 when the clip is missing or its rate is 0 |
| SetChannelRateScale | 0x11102730 | AnimRate = scale x base |
| StepBlendAlpha(ch, target, step) | 0x111029a0 | when \|alpha - target\| > step alpha moves by step toward target, else it becomes target |
| SetBlendAlpha(ch, a) | 0x111027d0 | alpha = clamp(a, 0, 1) |
| SetAnimSequence | 0x11102a10 | sequence, base and notify flag; frame, AnimRate, loop, backward kept |
| CopyAnimation(from, to) | 0x11102620 | copies sequence, anim set, AnimRate, base, frame, alpha, AnimLast, notify flag, finished, loop, TweenRate, OldAnimRate, the timed blend (flag, target, timer); not backward, mirror, StartBone or bNotifyIgnore |
| SetAnimFrame(ch, t, unit) | 0x11102870 | frame = t, or t / (N - 1) with the unit flag |
| EnableChannelNotify(ch, b) | 0x11101270 | bNotifyIgnore = !b |
| SetBlendParams | 0x111005a0 | channels above 0 only: alpha, InTime and OutTime (at most 1), StartBone (0 when the bone is not found), bGlobalPose, the timed blend cancelled |
| BlendToAlpha | 0x11102b50 | target, timer, timed blend on |
| ZeroMovementAlpha(from, to, step) | 0x110dd700 | channels from to to - 1 with alpha > 0 step toward 0; when none had alpha above 0, every channel of the range gets rate scale 0 and frame 0 |

A channel created on demand (ValidateAnimChannel 0x110fec90, channels 0 to 256) is all zero.

## A4. Tween cache (pose build 0x11109a50)

The pose build runs after the tick for every drawn channel with weight above 0 (channel 0 always), and for a
channel whose frame is below 0, once a previous pose exists (mirrored channels in a second pass, with the same
rule):

- t = 1 - frame / cachedFrame. When the cached sequence differs, or t is outside [0, 1]: cachedFrame = -1/N, cached
  sequence = the channel's, TweenWeight = 0, t = 0. Else cachedFrame = frame and TweenWeight += (1 - TweenWeight) t.
- Each included bone is slerped (rotation) and lerped (translation) from the previous drawn pose toward the
  channel's first key by t.

So TweenWeight equals 1 + frame x N while the tween runs (linear in the frame) and keeps its last value afterwards;
the recorder's channel field +0x64 is this value.

When it runs is not decoded; the goldens fit one build per drawn frame after the tick for a pawn that is drawn (the
spy), one right after the channel advance for the local merc seen from his eyes (not drawn; the bone lookups of his
attached rifle build it), and a build before the clips of an action that runs inside the tick (a landing: the falling
clip's weight moves on before the landing clip replaces it). The recorder can sample a frame before its build.

## A5. Sampled key position (pose build)

With f = clamp(frame, 0, 1): forward clips sample key f x N; backward clips sample (1 - f) x N, minus 1 (floor 0)
for a one-shot. Key N of a loop is its first key again (the wrap blends the last key into the first).

## A6. Walk blender (ASBasePawn::UpdateMovementAnimation 0x112008a0), complete

Run in the pawn's post-tick (bPhysicsAnimUpdate). V = Velocity - Base.Velocity. step = dt / BlendChangeTime.

1. The script event TurnHead runs first.
2. Health < 1 or bLyingOnGround: channels 1 to 24 StepBlend(0, 10 dt) and rate scale 0. Then fDistantTurnRate = 0.
3. fBlendTimeWithC0 > 0: StepBlend(ChannelForBlendWithC0, 1, dt / fBlendTimeWithC0) and ZeroMovementAlpha(4, 14,
   the same step). fDistantTurnRate = 0.
4. Otherwise, with c5 = channel 5's frame now (after this tick's advance):
   1. channel 0 without sequence: PlayAnim(0, AWaitSt0, 1, 0.1, loop);
   2. first call (bInitializeAnimation clear, then set): channels up to 24 created; AWaitSt0 loops with tween 0.1 on
      2, 3 (backward), 5, 10, 6 (backward), 11 (backward), 8, 13, 7 (backward for the spy), 12 (backward for the
      spy), 4 and 9, in that order; channels 2 to 13 get notifies off and alpha 0; channel 5 notifies on;
   3. a simulated proxy (Role 2) whose Physics changed relocks the root: iLockRootMotionType on entering RootMotion,
      0 on leaving it;
   4. bNoRunAnimation with Physics not Walking and V non-zero: StepBlend(1, 0, step); then when Velocity.Z < 0
      StepBlend(1, 0, 10) and bNoRunAnimation cleared;
   5. Vandamme with IK_Type 5: aim blend (A6.5) with yaw = (iYawPitchFromView low word - Yaw) x 8.333333e-5 and
      pitch = (high word + 0x951) x 1.0970927e-4, both clamped to [-1, 1]; then with bWeaponTransit channel 14
      (the split draw clip of actions 123 and 124) steps: played backward (the holster), toward 1 while the weapon
      is loaded and 0 after, at 5 dt; forward (the draw), toward 1 at 10 dt while unloaded and toward 0 at 3.3333 dt
      once loaded. Done. (The channel query is mesh instance slot 0xec; read as IsAnimBackward from what the two
      cases need, not from the vtable bytes.)
   6. PipHFire 3 with IK_Type 4: aim blend with the same scales (high word without offset). PipHFire 2 with IK_Type
      4: yaw from (low word - Yaw + 0x8000) x 6.1349696e-5 and pitch high word x 1e-4. Done.
   7. BTW set, or Ledge with bBeginSpecial12Anim: the shimmy blend (A6.6). FollowParent (12): A6.7. OnPipe or
      OnLadder with bBeginSpecial12Anim: the climb step (A6.8). RootMotion, Linear, OnPipe, OnLadder or Vandamme:
      ZeroMovementAlpha(2, 14, 10000). Each of these ends the call (then fDistantTurnRate = 0).
   8. Feet ledge (TimeSeconds - 2 < fTimeOfFeetLedge): StepBlend(4, 1, 1), channels 5 to 13 StepBlend(0, 1e9),
      channel 5's frame 0 with bIsLeftPart else 0.5 (that frame is also the c5 used below), fTimeOfFeetLedge =
      TimeSeconds - 3.
   9. bBeginSpecial12Anim is cleared.
   10. Falling, or TimeSeconds < MovementBlendStartTime: ZeroMovementAlpha(2, 14, step); in Falling with bIsRolling,
       once channel 15's frame passes 0.65: ZeroMovementAlpha(15, 16, step), bIsRolling cleared when channel 15's
       alpha is then 0, and at frame 1 or more channel 15 drops to 0 at once and bIsRolling is cleared. OldRotYaw =
       Yaw, fDistantTurnRate = 0. Done.
   11. Standing (V.x^2 + V.y^2 < 2 and, on a vertical plane, |V.z| < 1; or Ledge): with bNoRunAnimation channel 4
       steps to channel 1's alpha and channel 1 to 0 (step 10) and the flag clears; ZeroMovementAlpha(4, 14, step).
       In Walking (turn in place): step2 = dt / 0.15, alpha 0.75 crouched else 0.5; d = (Yaw - trunc(OldRotYaw)) as
       a signed 16-bit turn, r = d / dt; fDistantTurnRate = r when it was 0, when dt >= 0.3, for Role above 2 when dt
       >= 0.1, or for a locally viewed controller; else smoothed: Role below 3 (r dt + (0.3 - dt) old) / 0.3, Role 3
       or more (r dt + (0.1 - dt) old) / 0.1, and r takes the smoothed value. scale = \|r\| x 4.1666666e-5. r < -5000:
       channel 2 gets TurnAnim (SetAnimSequence), StepBlend(2, alpha, step2) and rate scale; else
       ZeroMovementAlpha(2, 3, step2). r > 5000: the same on channel 3; else ZeroMovementAlpha(3, 4, step2).
       OldRotYaw = Yaw (fDistantTurnRate kept). Other modes: ZeroMovementAlpha(2, 4, step), OldRotYaw = Yaw,
       fDistantTurnRate = 0. Done.
   12. Moving: ZeroMovementAlpha(2, 4, step), OldRotYaw = Yaw, fDistantTurnRate = 0. X = the facing (Rotation),
       flattened in Walking, OnVerticalPlane and FollowParent, normalised; Y = X cross (0, 0, 1) normalised (the
       pawn's left); D = Velocity normalised.
   13. Modes other than Walking, FollowParent, OnVerticalPlane and Repositioning (OnPipeH in practice): f = D.X, s =
       D.Y, wf = f^2, ws = s^2; OnPipeH: ws = 0, f = sign(f) negated when fBlendCoeffWalkAnim < 120, wf = f^2.
       wf > 0 becomes 1, and with ws > 0.1 ws = (1 - ws) bias + ws (BackwardStrafeBias for f <= 0, else
       ForwardStrafeBias). Set changes: ForwardAnim onto 5 and 6 (each first copied to 10 or 11 when that copy's alpha
       is below the channel's), RightAnim onto 7 and 8 (7 is copied from channel 5 to 12, 8 to 13, under the same
       alpha test). Alphas: 5 wf when f >= 0 else 6 wf (the other 0); 7 ws when s >= 0 else 8 ws; 9 to 13 and 4 to 0;
       rate scale of 4 to 13 = clip rate of 5 / clip rate of the channel; frames of 6 to 13 = c5. Done.
   14. Walking, FollowParent, OnVerticalPlane, Repositioning: f = X . Velocity, s = Y . Velocity (f = Velocity.Z on
       a vertical plane); with bNoRunAnimation StepBlend(1, 1, 10 dt). R = (\|f\| + \|s\|) / fBlendCoeffWalkAnim;
       w = 1 / (\|f / s\| + 1) when \|s\| > 1, else 0. Set changes, each copying the outgoing clip with its alpha:
       ForwardAnim (5 to 10, 6 to 11), RightAnim (spy: 7 to 12; both: 8 to 13), LeftAnim (merc: 7 to 12), RunAnim (4
       to 9; with bNoRunAnimation channel 4 first takes channel 1's alpha, channel 1 goes to 0 and the flag clears).
       scale = 1, wf = 1; slow walk when fRunSpeedLimit > fWalkSpeedLimit, not crouched, R < 1 and
       fBlendCoeffWalkAnim is not 385: scale = R, R = 1, and wf = 1 - w when scale < 1.
       - f < 0: 5 to 0, 6 to wf x scale, 4 to 0.
       - scale >= 1: run fraction q = 0 when \|w\| >= 0.7 or RunAnim is None; else q = clamp((\|f\| - walk) / (run -
         walk)) (limits reversed: 1 - clamp((\|f\| - run) / (walk - run))), wf = 1 - q, R = (\|f\| + \|s\|) (wf /
         fBlendCoeffWalkAnim + q / fBlendCoeffRunAnim); 4 to 1 when q > 0 (to 0 at once with bNoRunAnimation), else
         to 0; 5 to wf; 6 to 0.
       - slow walk (scale < 1): 4 to 0 (5 when the limits are reversed); then 5 to wf x scale (4 when reversed), 6 to 0.
       - 7 to scale x w when s >= 0, else 8; the other 0; 9 to 13 to 0.
       - rate scale of 5 = R; frames of 6 to 13 and of 4 = c5 (and channel 1 with bNoRunAnimation).
       All alpha moves are StepBlendAlpha with step unless noted.
5. Aim blend (0x111ffa90, Vandamme and horizontal-pipe fire): rate scale 1 on 5, 8, 10, 13; max step 1.4285715 dt (5
   dt with bWeaponTransit); ZeroMovementAlpha at once on 2-3, 6-7, 9, 11-12, 4. RunAnim onto 5, RightAnim onto 8,
   ForwardAnim onto 10, LeftAnim onto 13 (each to alpha 0 on a change); frames of 8, 10, 13 = c5. With yaw a and
   pitch b: when -a >= b the up-left corner: a' = -a, b' = -b; else a' = a, b' = b. When a' + b' >= 1.9999 the
   corner weight is 1 and the side weight 0; else m = 2 - a' - b', k = (b' - a') / m, side = 1 - \|(a' - 1) / m\|,
   corner = 1 - sqrt(((a' - 1)^2 + (b' - 1)^2) / ((k - 1)^2 + (k + 1)^2)). Channel 13 (first case) or 10 takes the
   corner weight, the other of the two 0, channel 5 1 and channel 8 the side weight.
6. Shimmy blend (0x111ffe80, back to wall and the ledge shimmy): step = dt / BlendChangeTime, 4 dt when BlendChangeTime
   is 20; at once to 0: 2-3, 6-7, 9, 11-12, 4; rate scale 1 on 5 and 8; 10 and 13 step to 0. RightAnim onto 5 (old
   copied to 10), LeftAnim onto 8 (old copied to 13). Side speed u = X.y Vx - X.x Vy with X the facing axis of the
   rotation (u > 0 is toward the pawn's left): u > 1 moves 8 to 1, u < -1 moves 5 to 1, the other to 0 (not when BlendChangeTime is 20); with
   either above 0 the frames of 5, 8, 10, 13 = c5; rate scale of 5 = \|Velocity\| / fBlendCoeffWalkAnim.
7. Follow-parent blend (0x112004b0, the grabbed merc): ForwardAnim onto 5 and 6 (a change first zeroes 1 to 14 at
   once), RightAnim onto 8 and LeftAnim onto 9 when RightAnim changed. Moving (and channel 0 not playing the catch clip
   GrbdStAlBg0): forward weight on 5 (6 when backward), the side weight 1 / (\|f / s\| + 1) on 9 (left) or 8 (right) with 2
   to 0, rate scale 1, frames of 5 and 6 = c5, channel 0's frame rewritten unchanged. Still: 1, 2, 3, 5, 6, 9, 8 step
   to 0.
8. Climb step (0x11200140, pipe and ladder holds with bBeginSpecial12Anim): 2-3, 7-13, 4 to 0 at once; rate scale 1 on
   5; ForwardAnim onto 5 and 6. Velocity.Z 0: 5 and 6 to 0 at once, rate scale 0 on 5, fOldFrame -1, bIsStopped and
   bCanStop set. Moving: after a stop the frame restarts at clip rate x dt / N (+ 0.5 with bIsLeftPart), bIsStopped
   clears; going up 5 to 1 and 6 to 0, down the reverse; frames of 5 and 6 = that frame; bCanStop set on crossing 0.45
   upward, on a wrap below 0.5 or past 0.95; entering (0.25, 0.75) sets bIsLeftPart and clears channel 0's mirror,
   leaving it clears bIsLeftPart and sets channel 0's mirror; fOldFrame = the frame.

## A8. SBasePawn.AnimateBTW (script event)

Run by the back-to-wall actions (180 to 182, 184, 186, 193, 194; execute-action.md). It sets the shimmy set the walk
blender's shimmy blend reads (A6.6): crouched RightAnim BackCrAlFdL, LeftAnim BackCrAlFdR, fBlendCoeffWalkAnim 75;
standing BackStAlFdL, BackStAlFdR and 130. Then it loops the hold clip of the BTW value on channel 0 (LoopAnim, rate
1): BTWStand BackStAlNt0 and BTWCrouch BackCrAlNt0, without tween when OldBTW is NOBTW (entering) and over 0.2 s
otherwise; the peeks BackStPkNtL, BackStPkNtR, BackCrPkNtL, BackCrPkNtR and the fire poses BackStSpNtL, BackStSpNtR,
BackCrSpNtL, BackCrSpNtR over 0.3 s. Last OldBTW = BTW. Evidence: SBase.u SBasePawn.AnimateBTW (bytecode
3a3298860753843298edc55e70b3966c68cff713); golden traversal.spy-btw-peek (the stand loop starts untweened from the
enter clip's AnimEnd, the shimmy channels carry BackStAlFdL and BackStAlFdR).

## A9. The post-tick (editor 0x11205ef0)

After UpdateMovementAnimation (when bPhysicsAnimUpdate and a mesh): OldPhysics = Physics, OldRotYaw = Rotation.Yaw,
the old acceleration = Acceleration, and the script event UpdateEyeHeight when bUpdateEyeheight. So the turn in place
measures the yaw change of one tick (the controller's turn and the root-motion turn and end placement alike).

## A7. What the goldens record

re/tools/difftest/livegame.py reads the mesh instance channel array. `anim` holds channel 0 and every channel with
alpha above 0: [channel, sequence, frame, AnimRate, alpha (1 for channel 0), and for channel 0 TweenWeight]. The
traversal extra `ach` holds channels 0 to 15 that have a sequence, whatever their alpha: [channel, sequence, frame,
AnimRate, BlendAlpha (raw, 0 for channel 0), TweenWeight, TweenRate]. AnimRate is normalised (frames per second / N x
play rate).

## A10. Root position of a clip (root track, absolute keys)

Measured on the moveset goldens (ANSWERS-moveset.md findings 1 and 2) and ported in PawnClip.RootAt and
PawnRootMotion.

1. A clip with a keyed RootTrack (`rootTrack.time` not empty in svm.anim/1; 75 spy and 19 merc clips) gives the
   root position from the track: the key positions at their frame numbers, linear between keys, the first key
   before it and the last key after it. The root bone is not used for that clip's movement.
2. A clip without a track uses its root bone, one key per frame, linear between frames.
3. Both are absolute: no clip is moved to start at zero. While the root stays locked, a new clip on channel 0 takes
   its first delta from the previous clip's last root position (spy-wall-jump frame 36: WljpStAlBgR's track ends 50
   forward, WljpStAlUpR's bone starts 40.69 forward and 23.31 up, the pawn moves 9.3 back and 23.3 up in one frame).
4. Mesh space is Y forward, X to the pawn's left: pawn space is (y, -x, z) for the track as for the bone.
5. Not used yet: the track's rotation keys (LaddStAlBu0 turns half a turn in its track). The turn comes from the
   root bone's keys (docs/ANIMATION.md, root motion).

## A11. SBasePawn.AnimEnd (script event), complete

The pawn's part, before the controller's AnimEnd (ported in src/OpenScct.Game/Animation/PawnAnimEnd.cs):

| Channel | What happens |
|---|---|
| 16 (reanimate) | AnimBlendParams(16, 0) |
| 24 (interact) | blend to 0 over 0.2 s when ButtStAIFd0 or ButtStAIFd2 ended backward, or MineStAlEd0 or MineCrAlEd0 ended |
| 21 (weapon) | sAction = 0; blend to 0 over 0.2 s; IK_Type = IKT_WEAPONLOADED after ReloStSpFd2, ReloStSpFd1, ReloStSpFd2SU, ReloStSpFd2S or ReloStSpFd3 |
| 19 (attack) | blend to 0 over 0.2 s |
| 4 (run) | BlendChangeTime = 0.25 |
| 18 (goggle) | sAction = 0; bGogglesUp toggles; blend to 0 over 0.2 s |
| 17 (damage) | sAction = 0; blend to 0 over 0.3 s; IK_Type = IK_TypeBeforeDamage when IK_Type is IKT_NONE; IK_TypeBeforeDamage = IKT_NONE |
| 14 (draw) | AnimBlendParams(14, 0); bWeaponTransit off; without BTW, IK_Type IKT_BTW or with bStickyShocking BTW = NOBTW; else BackSt/CrSpBgL/R forward leads to the fire hold (BTWFire*), backward to the peek (BTWStand/CrouchLeft/Right), then AnimateBTW |
| 1 (no-run overlay) | with bNoRunAnimation: clear it, AnimBlendParams(1, 0) and, walking, AnimBlendParams(4, 1) |
| 0 | SpltStAlRt0/Lt0: PlayAnim(AFall or AFall2H, , 0.35); HackStAlBg0: LoopAnim HackStAlNt0; BackCrAlTr0/BackStAlTr0: PlayAnim(the stance's turn, , 0.2, , backward); AtakDnMmBg2: Walking, LoopAnim AtakDnMmNt2; AtkdDnMmBg0: LoopAnim AtkdDnMmNt0; a spy after XxxxDnAlFd0, any spy death clip, XxxxCrAlAk0, XxxxStAlAk0 or StrkCrAlBk0 (a merc after StrkCrAlBk0): Walking, bInternalChangePhysics, PlayAnim XxxxDnAlBk0; GrbdStLgRail (merc): Falling, Velocity 77.5 X with Z -681, PlayAnim FallStAlFr0, the controller's m_LastSpecialMoveTime and state s_FallAfterGrabbedFromLedge |
| 22 (palm) | blend to 0 over 0.1 s after ThroStAlMix |

## A12. AnimateStandUp (editor 0x111f8ce0), the merc's table

Merc (SPawnType 1), by channel 0's clip; "RM" = PHYS_None then PHYS_RootMotion, lock 1, +W, end0. The first match
wins; the merc's 2H death fields hold the same clips as the plain ones. ADeathCr{Forward, Left, Right, Down} to
AWakeCr*; XxxxCrAlAk0 to WakeCrSpFd0; ADeathCrBack or StrkCrAlBk0 with StrkType 0: RM, AWakeCrBack; ADeathStForward
to AWakeStForward; ADeathStBack, StrkCrAlBk0 with StrkType 1, or GrabStMmEd1: RM, AWakeStBack; ADeathSt{Left, Right,
Down}; ADeathCrForward2H; ADeathCrBack2H or StrkType 2: RM, AWakeCrBack2H; ADeathCr{Left, Right, Down}2H,
ADeathStForward2H; ADeathStBack2H or StrkType 3: RM, AWakeStBack2H; ADeathSt{Left, Right, Down}2H; GrbdStAlXxa,
GrbdStAlXxb or GrbdStAlKo0 to GrbdStAlWkA; XxxxStAlAk0, XxxxStAlAk2, XxxxCrAlAk2 to WakeStSpFd0, WakeStSpFd2,
WakeCrSpFd2; XxxxDnAlBk0 to AWakeCrBack2H crouched or AWakeStBack2H (no root motion); LandStXxBk2: RM, WakeStSpBk2;
any other clip: nothing. The spy always goes RM and AWake{St, Cr}Back{, 2H} by stance and WeaponLoaded. The clip is
played on channel 0 at rate 1, no tween. Name indices resolved through the editor's name table (local/reflect).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Notify window margin (backward) | 1e-6 | normalised | decoded | 0x111056c0 |
| ClipRate fallback | 30 | frames/s | decoded | 0x11105660 |
| Init tween of the walk channels | 0.1 | s | decoded | 0x112008a0 |
| Turn-in-place step | dt / 0.15 (6.6666665 dt) | alpha | decoded | 0x112008a0 |
| Turn rate smoothing windows | 0.3 (simulated proxy), 0.1 (authority) | s | decoded | 0x112008a0 |
| Strafe bias threshold | 0.1 | weight^2 | decoded | 0x112008a0 |
| Run fade-in cut | \|w\| >= 0.7 | weight | decoded | 0x112008a0 |
| Charge set coefficient (no slow walk) | 385 | - | decoded | 0x112008a0 |
| Aim steps | 1.4285715 dt, 5 dt with bWeaponTransit | alpha | decoded | 0x111ffa90 |
| Aim saturation | a' + b' >= 1.9999 | - | decoded | 0x111ffa90 |
| Shimmy corner step | 4 dt when BlendChangeTime is 20 | alpha | decoded | 0x111ffe80 |
| Feet ledge window | 2 s, then reset to 3 s ago | s | decoded | 0x112008a0 |

## Evidence

- Editor: PlayAnim 0x110ff8f0, UpdateAnimation 0x111056c0, pose build 0x11109a50, ValidateAnimChannel 0x110fec90,
  IsAnimating 0x110fed00, SetAnimSequence 0x11102a10, CopyAnimation 0x11102620, SetChannelRateScale 0x11102730,
  StepBlendAlpha 0x111029a0, SetBlendAlpha 0x111027d0, ClipRate 0x11105660, SetBlendParams 0x111005a0,
  BlendToAlpha 0x11102b50, ZeroMovementAlpha 0x110dd700, NotifyAnimEnd 0x11208a60, UpdateMovementAnimation
  0x112008a0, aim blend 0x111ffa90, shimmy blend 0x111ffe80, follow-parent blend 0x112004b0, climb step 0x11200140,
  AnimateWalking 0x111fd360, controller has a viewport 0x11113d40, float-to-int truncation 0x1123fb80.
- Goldens (local/difftest/golden, golden-traversal): the run start of movement.spy-run-forward (slow walk at R
  0.456 and 0.911, then the run fraction (|f| - 110) / 180 and the mixed rate), the turn in place after the ledge
  pull-up, the climb channels of traversal.spy-pipe-climb-down, the roll blend on channel 15 and the after-roll copy
  on channel 9 of traversal.spy-roll; per-frame comparison in tests/OpenScct.DiffTest/Anim.

## Open

1. bGlobalPose remains estimated in the client. The mirror reflection is resolved in anim.md rule 3.3: mapped
   mesh-space bones, excluded root, then parent-relative reconstruction before tweening and blending.
2. The script event TurnHead (head look-at) is not ported; it only moves the head bone.
3. The Vandamme weapon channel (A6 step 4.5) is decoded from one branch only; no golden covers it.
