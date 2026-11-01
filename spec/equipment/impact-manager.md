# Bullet and shock impacts (SImpactManager, SImpactEffectBase, SBulletImpact)

Units:
- uc:Engine.SImpactManager:PostBeginPlay, uc:Engine.SImpactManager:AddImpact, uc:Engine.SImpactManager:Timer,
  uc:Engine.SImpactManager:LogTab, def:Engine.SImpactManager, rep:Engine.SImpactManager.AddImpact
- uc:Engine.LevelInfo:PostBeginPlay (the manager's spawn)
- uc:Engine.SImpactEffectBase:Effects, uc:Engine.SImpactEffectBase:Timer, def:Engine.SImpactEffectBase, def:Engine.SBulletImpact
- def:SBase.SImpactEffectBeton, def:SBase.SImpactEffectMetal, def:SBase.SImpactEffectSoft, def:SBase.SImpactEffectGlass,
  def:SBase.SImpactEffectWood, def:SBase.SImpactEffectFence, def:SBase.SImpactEffectWater, def:SBase.SImpactSteamPipe,
  def:SBase.SImpactWaterPipe, def:SBase.SImpactEffectSand, def:SBase.SImpactEffectHole, def:SBase.SImpactEffectHoleBack,
  def:SBase.SImpactEffectShocker, def:SBase.SImpactEffectShockerMetal

## Purpose
Every bullet and Shocker hit on the world leaves a mark on every machine: a small decal projected on the surface, a
spark burst, and the impact sound heard after the sound's travel time. The weapons do not spawn the effects
themselves; they ask the level's one impact manager, which keeps a ring of 50 reusable marks that fade after 15 s.

## Data
| Field | Class | Default | Notes |
|---|---|---|---|
| TABSIZE | SImpactManager const | 50 | marks kept at once |
| Lifetime | SImpactManager const | 15 | seconds a mark stays |
| NBImpact | SImpactManager | 0 | unused |
| TabTimer[50] | SImpactManager | -1 | the whole second a slot expires, -1 free |
| TabImpact[50] / TabProj[50] | SImpactManager | | the slot's effect actor and decal projector |
| RemoteRole / bAlwaysRelevant / bHidden | SImpactManager | SimulatedProxy / true / true | |
| SndBullet, SparkEmitter, ImpactMaterial | SImpactEffectBase | None | per impact class (table below) |
| DrawType / RemoteRole | SImpactEffectBase | DT_None / ROLE_None | |
| Lifetime, FOV, MaxTraceDistance, DrawScale | SBulletImpact | 30, 1, 20, 0.1 | modulate-blended projector clipped to BSP and static meshes, border clamp |

Impact classes (class defaults; materials and sounds are package paths):

| Class | ImpactMaterial | SparkEmitter | SndBullet | Mesh |
|---|---|---|---|---|
| SImpactEffectBeton | sfx.Impacts.decal_beton | SSparkBeton | RA_impactShot_ConH | |
| SImpactEffectMetal | sfx.Impacts.decal_metal | SMetalSpark | RA_ImpactShot_MetS | |
| SImpactEffectWater | none | SSparkWater | RA_ImpactShot_Water | |
| SImpactEffectHole | sfx.Impacts.decal_wood | SSparkWood | RA_ImpactShot_Carton | light_hole, scale 4, unlit |
| SImpactEffectHoleBack | sfx.Impacts.decal_wood | SSparkWood | RA_ImpactShot_Carton | |
| SImpactEffectShocker, SImpactEffectShockerMetal | sfx.Impacts.Burn_sticky | SSparkShocker, SSparkShockerMetal | none | |
| others | read from the generated class defaults | | | |

## Rules
1. **Spawn.** The server's LevelInfo spawns the one manager in PostBeginPlay (not in the entry level). Clients get it
   by replication (always relevant).
2. **Setup** (PostBeginPlay, every machine that has the manager): 50 slots, each with a hidden impact effect actor
   and a hidden decal projector at the origin, every timer -1; then a looping 1 s timer.
3. **AddImpact(Type, Loc, Rot)** is a reliable call from the server that goes to every client (the SCCT broadcast
   flag, spec/net/model.md rule 10) and also runs on a listen server. On each machine:
   1. Pick a slot: the first free one (timer -1); if none is free, the one with the smallest timer (the oldest; the
      first of equal ones).
   2. A reused slot hides its effect and decal first.
   3. Timer = `int(now + 15)`.
   4. The slot's effect takes the type's SndBullet, ImpactMaterial and SparkEmitter, and moves to Loc and Rot. A
      type drawn as a static mesh (the bullet hole) shows that mesh with the type's draw scale and unlit flag;
      any other type keeps the effect hidden.
   5. The decal takes the slot effect's ImpactMaterial, sits 10 uu along Rot's forward axis from Loc (Rot faces out
      of the surface, so the decal sits just in front of it) and projects back along -X, and is shown.
      A shown projector attaches to the colliding static-mesh actors and BSP in its frustum and is drawn as part of
      them (UE2 projector behaviour, not decoded for this build). So a decal on a gameplay object is not drawn while
      that object is not drawn (a broken vase, DrawType DT_None), and the shot that breaks a one-hit object leaves no
      decal on it (its collision is already off). The port records the actor the trace hit with the mark (sent with
      the call) instead of projecting. Pawn hits keep their target identity and spawn the impact sparks without a
      decal, since the projector does not attach to skeletal meshes. This also applies to predicted Shocker hits.
   6. Effects(): spawn the type's spark emitter at the mark, then play SndBullet (volume 0.8, radius 100) after
      `distance to the local player's controller / 43561` seconds.
4. **Timer** (every second): each slot whose timer has passed hides its effect and decal and is freed.
5. **Callers.** The rifle trace (merc-rifle.md rule 10.8) adds its surface class at the hit point (with the hit
   normal's rotation and a random roll) and the two bullet holes of a fake ceiling; the Shocker trace
   (spy-shocker.md rule 11.4 and 11.6) adds SImpactEffectShocker or SImpactEffectShockerMetal 10 uu out from the
   hit. Nothing else calls it in SBase.

## States
None (the manager has no states; slots are free or timed).

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Slots | 50 | | decoded | TABSIZE |
| Mark life | 15 | s (whole seconds) | decoded | Lifetime const, AddImpact |
| Decal offset | 10 | uu | decoded | AddImpact |
| Sound speed | 43561 | uu/s | decoded | SImpactEffectBase.Effects |
| Impact sound | volume 0.8, radius 100 | | decoded | SImpactEffectBase.Timer |
| Cleanup period | 1 | s | decoded | PostBeginPlay |

## Network
- Server: decides every impact (the traces run there) and broadcasts it.
- Every client with the manager's channel runs AddImpact; a listen server runs it too; a dedicated server does not
  draw.
- The effect actors and decals are local to each machine (RemoteRole None), so a client that joins later sees only
  impacts made after it.
- Port note: our combat bridge carries owner-only calls, so the broadcast is sent as one server-to-owner call per
  connected player (the same set of machines as the manager's channels, since the manager is always relevant).
- Pawn impacts also carry a decal-suppression flag, so a destroyed pawn whose actor reference arrives as null
  cannot leave a floating world decal.

## Edge cases
- When all 50 slots are busy the oldest mark is replaced at once, even if it is younger than 15 s.
- Timers are whole seconds, so a mark lasts between 14 and 16 s depending on when the 1 s timer runs.
- The script reads the decal material through the loop index rather than the chosen slot; when a free slot was found
  they are the same; when the table was full the index is past the end. The port uses the chosen slot's material.

## Evidence
- local/re-out/uc/Engine/SImpactManager.uc, SImpactEffectBase.uc, SBulletImpact.uc, LevelInfo.uc 366-373;
  SBase/SImpactEffect*.uc; local/formats/classdefaults_effective.json; spec/net/model.md rule 10 (the 20 broadcast
  functions).

## Open questions
- The spark emitters (SSpark*, SMetalSpark) are particle classes from the effect packages; their exact particles are
  not converted yet, so the view draws a generic spark burst.
- Which machines run the native decal clipping (BSP and static meshes only, so never on pawns).
