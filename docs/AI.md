# AI bots

The original game has no bots. SVM adds them so a player can fill a match alone and so matches can be played
headless for tests. The bots are meant to play fair: they act only through a player's input and perceive only what a
player could see or hear. Their utility decisions, observed contact snapshots and spatial searches run locally,
with difficulty presets and without gameplay recordings, trained models, a GPU or an external AI service.

```powershell
OpenSCCT.exe -- --svm-play=SteeE --side=spy --bots=3 --difficulty=normal   # a 2v2 with three bots
```

## Principles

- **Server-side controllers on the player input path.** A bot is an `SPlayerController` logged in through the game's
  own `Login` and `PostLogin`, with an `AiViewport` as its player object (a player of the server's machine, never the
  level's local player). Before every world tick the bot writes the same input bytes a human's input layer writes
  (`BT_Up`, `bCrouch`, `bUsing`, `bFire`, `bFireMode`...), the view rotation a mouse would produce, and the exec
  commands a key press runs (`Jump`, `Fire`, `AltFire`, `SelectGadget`, `GadgetQuickChoice`, `SwitchHeadset`,
  `GadTorcheSwitch`). `NativeComputeControlVariables`, the movement states and the combat code then do the rest, so
  every movement and combat rule binds the bots. The view turns no faster than the difficulty's turn rate.
- **No hidden state.** Sight goes through the bot's own view (rotation, FovAngle), a line of sight, range and the light
  on the target; hearing through the sounds and noises of the level; objectives through what the HUD shows (the merc
  objective radar, the localized messages). The existence of the other players is public (the scoreboard); their
  positions are not read unless perceived.
- **Godot-free and deterministic.** `src/OpenScct.Game/AI` follows the simulation rules (no Godot, no banned APIs, seeded
  random streams from the match seed and the bot index, ordered collections where order matters).

## Layout

| Path | What |
|---|---|
| `src/OpenScct.Game/AI/AiController.cs` | one bot: perception, memory, the side's brain, turn-rate limited view, stance key |
| `src/OpenScct.Game/AI/AiInput.cs` | a tick of input (bytes, view, aim, two commands) and `AiViewport` |
| `src/OpenScct.Game/AI/AiWorld.cs` | per level: graphs, light field, noise board, team boards, HUD message tap, smoke and torch lists |
| `src/OpenScct.Game/AI/AiLightField.cs` | the pawn light level by the renderer's rules (`Lighting/ActorLightRules.cs`): zone ambient, four strongest lights, spot shadow lights and torches, trigger light switches |
| `src/OpenScct.Game/AI/AiPerception.cs` | `AiSight`, `AiNoiseBoard` (MakeNoise hearing and sounds), `AiMemory` |
| `src/OpenScct.Game/AI/AiSearch.cs` | reachable search locations, observed travel direction, visual inspection and teammate assignments |
| `src/OpenScct.Game/AI/NavBuilder.cs`, `NavGraph.cs`, `NavFollower.cs` | navigation from collision and GEs, A*, following with keys |
| `src/OpenScct.Game/AI/DoorAi.cs` | closed doors on a route: wait, hack the door's switch, or shut its edges |
| `src/OpenScct.Game/AI/AiBrain.cs`, `SpyAi.cs`, `MercAi.cs` | utility choice and the two sides' goals |
| `src/OpenScct.Game/AI/Evasion.cs`, `SpyGadgets.cs` | a spy's play round the motion tracker; its smoke, flash, chaff, heartbeat and sticky cam |
| `src/OpenScct.Game/AI/TeamAi.cs` | team play: revive, heal and unspy a mate, climb on a mate's boost or hoist, offer a boost |
| `src/OpenScct.Game/AI/AiDifficulty.cs`, `AiK.cs` | presets and constants (decoded ones carry their provenance) |
| `src/OpenScct.Bots/ServerBots.cs` | adds bots to a `ServerHost` (login, per-tick drive) |
| `src/OpenScct.Bots/AiNavCache.cs`, `AiMapLight.cs` | graphs per map content hash with a disk cache; lights from `render/lighting.json` |
| `src/OpenScct.Bots/AiBotMatch.cs` | a headless bot-versus-bot match with a report |
| `game/Scripts/Play` | `--bots=` and `--difficulty=` for offline play |
| `tests/OpenScct.Game.Tests/AI` | perception, navigation, following and match tests |

The older client bots of `OpenScct.Bots` (`BotClient`, `SpyBrain`, `MercBrain`, `NavGrid`) stay: they are scripted
network test players for the netcode checks (docs/NETCODE.md, "Bot matches").

## Perception

### Sight (`AiSight.Look`)

1. On screen: body samples inside a rectangular view, with the renderer's 4:3 reference FovAngle and Hor+ at a fixed
   16:9 bot viewport. Pitch and both screen axes count. Inside the difficulty's focus angle a target counts fully;
   outside it (peripheral) range and strength are scaled by the peripheral factor.
2. In range: the difficulty's sight range (thermal: at most 5000 uu, its fade depth).
3. A line: from the eye (location + 60 standing, + 30 crouched) to five body samples (head, chest, feet, shoulders)
   through level geometry and movers; each sample must also be on screen. A prone body's samples follow the body
   along the floor, below its unchanged collision cylinder's centre. The share that is open scales strength. Smoke clouds (a 300 uu
   sphere round each `SSmokeEmitter`) block normal, night and motion vision; thermal sees through at 0.8; EMF draws no
   particles.
4. Lit enough, by vision mode:

| Mode (RendMap) | Shows the target when |
|---|---|
| Normal (5) | strength is `clamp(L / (LightThreshold x (0.5 + d / 1000)), 0, 1)`; dim light accumulates evidence slowly instead of falling below a hard invisibility cutoff. A running target shows 1.5 times better; within 100 uu on the floor (and 250 uu in all) a dark shape shows anyway |
| Night (34) | as normal with the light boosted: `(L + 1.4 x 30 / 255) x 4` (the mode's boost and post gain) |
| Thermal (36) | always (pawns are hot), strength `1 - d / 5000` |
| Motion (37) | the normal scene is still drawn under the blurred colour pass: ordinary body sight at 0.65 strength, plus the bot's own tracker brackets at full strength (`AiMotionSense`, spec/equipment/motion-tracking.md). A stationary or prone body can therefore be seen without a lock. Fully cloaked spy bodies are hidden, matching the renderer. A lock out of view is only a beep |
| EMF (38) | a spy only with its electronics on (`PawnVision.IsSpyEmfVisible`: weapon out, goggles on, binoculars, spy bullet, cloak, phosphorus); mercs always |

A cloaked spy (`GlassEffect`) is up to 85 % harder to see in normal view; night vision draws the opaque body as the
renderer does. Each look feeds a spotting meter
that fills in `SpotSeconds / strength`; at 1 the contact is confirmed and its position known exactly.

### Light (`AiLightField`)

The rules the renderer lights pawns with, Godot-free in `OpenScct.Game.Lighting.ActorLightRules`, which the renderer's
`ActorLighting`, `SpotShadowLight` and `WorldLights` call too (docs/RENDERING.md, "Actor lighting", "Dynamic lights",
"Spot shadow lights"). A pawn's term is `min(2, min(2, A + four lights) + spot shadow lights)`, the level its
luminance:

- **A**: the zone ambient of the pawn's zone (m_RefAmbientColor / 255, each channel at most 1; pawns take no
  AmbientGlow). The 2x `ZoneAmbientVertexGain` is for baked static meshes, not actors.
- **Four lights**: the lights kept for run time, `bApplyToInGameLighting` or `bDynamicLight`, with a light type, not
  ESpotShadow (until now the bots took only the in-game ones); own-zone lights only in their zone; ranked by
  `brightness x (1 - d^2 / (R + r)^2)`, suns first, spots outside their cone at 0; each adds
  `k C S(d / R) max(0, N.L)` (k 2, suns 1.75; C the FGetHSV colour; R = 25 (LightRadius + 1); S the smoothstep; the
  spot ramp from phi to theta = 0.05 phi), LE_QuadraticNonIncidence with `1 / (8 d^2 / R^2)`, LE_PointAmbient also
  its attenuated colour whatever the normal. The light type scales the colour at the level's time (pulse, subtle
  pulse, blink; flicker at its mean 0.375, strobe on).
- **Spot shadow lights**: every map LE_ESpotShadow light with brightness above 0 (window and sun projectors,
  searchlight patches) and every other merc torch that is on: `2 C' x attenuation x mix(1, N.L, influence)` inside
  the decoded frustum (apex `p x Range / (1 - p)` behind the light, +-SpotWidth x +-SpotHeight at the range: the torch
  lights 800 x 800 uu at 1500 uu, where the bots read 400 x 400 before), C' the colour scaled so its brightest channel
  is at most 1, the attenuation the linear or late ramp over the depth past the light, and only where the line from
  the light (the torch: the merc's eye) to the pawn is open. Own-zone limits do not apply to actors (as on screen).
  The cookie is taken as 1 (its texture is not on the server).
- **Switches**: every STriggerLight of the map switches the lights of its name (its actor id), as the client's
  `DeviceViews` does; the field follows them each tick (`AiWorld.BeginTick`).

N faces the observer, so a spy lit from behind stays dark to a merc in front. Route costs and hiding spots
(`LevelAround`) count every light as facing and take animated lights at their mean. The lights come from the map's
`render/lighting.json`; a server with only the sim part reads every point as 0.6 (`AiLightField.Unknown`), plus the
torches.

The original has no light meter for its AI to copy: Versus has no bots, `SCamera.CheckPresence` (game 0x10ab5df0)
detects by cone, range and an open line with no light test, and `Pawn.Visibility` / `bLightingVisibility` are only
read by `MakeNoise` (Visibility below 2 scales loudness by 0.3); no decoded native sets them from the lighting. The screen
is the only judge of how lit a spy is, so the bots read the screen's rules.

### Hearing (`AiNoiseBoard`)

- **MakeNoise**, decoded in `spec/native/make-noise.md`: server only, needs an Instigator, dampened by its
  `SoundDampening` when it made the noise, x 0.3 when its `Visibility` is below 2; `CheckNoiseHearing` keeps two noise
  slots per instigator and drops repeats within 0.2 s and 50 uu that are not louder than 1 / 0.9. The native has no
  distance test (the original delivers `HearNoise`, which no SBase controller implements); bots hear a noise within
  `HearingThreshold x sqrt(Loudness)` (2800 uu at loudness 1, the Pawn default), halved through walls, scaled by the
  difficulty. `Actor.MakeNoise` now runs the decoded part and hands the noise to the level's `INoiseHearing` (none
  outside bot matches, so nothing changes for players).
- **Sounds**: every `PlaySound` event of the last tick, as a player's audio plays it (spec/native/audio.md): radius 80
  for 0, full volume within the radius and `R / (R + 0.5 (d - R))` beyond, halved through walls; heard above the
  difficulty's threshold. The crouched and walking footsteps resolve to no sound in the original and are not heard;
  running steps, gear, landings, gunfire, doors and gadgets are.
- A heard noise gives a blurred position (the farther and fainter, the vaguer) and a confidence up to 0.85; noises of
  the bot's own side are ignored.

### Memory and team

`AiMemory` keeps one contact per enemy: position, time, velocity, confidence (halving every `MemoryHalfLife` seconds
without news) and the spotting meter. Confirmed sightings are called out on the team board; teammates take them with
the teamwork knob's weight and delay. Mercs see terminals being hacked as the HUD's objective radar shows them (the
trigger's replicated `bTriggering`); localized messages to bot controllers are tapped from the objective net calls
(`AiObjectiveTap`).

Direct confirmed sight also stores an immutable `AiObservedPose`: position, velocity, facing, stance and visible
combat state. Remembered grabs, drops, evasion, gadget aim and finishing approaches use these observations rather
than following a hidden enemy's live transform. A tracker bracket supplies a position, not hidden posture or health.
Contacts and team callouts expire by time, without polling an unseen enemy's health. Repeated or older callouts cannot
refresh confidence or rewind newer evidence. Missing pawns are marked unseen at the end of every sight scan.

A brief loss of sight preserves recognition for 1.5 s and the original reaction start for gaps below 0.5 s. A merc
inspects an unconfirmed visible shape before attacking, rather than turning away or walking through it while its
spotting meter fills. When approaching a downed spy it watches the remembered prone body instead of the standing
collision centre, and uses normal vision and the torch when appropriate.

### Searching after a lost contact (`AiSearch`)

Search locations spread from the last observed position through the side's navigation graph. The uncertainty radius
grows with elapsed time (250 to 1200 uu), with the last observed velocity a directional prior and travel distance a
cost. Each expansion visits at most 4096 graph entries and retains at most 32 spaced locations. Failed route choices
are limited to one per 0.4 s; the normal follower still handles doors and blocked edges.

Looking at a location supplies negative evidence only through the bot's view, clear geometry, smoke, range and
lighting, using the same spotting time. EMF cannot rule out an unarmed spy. Inspected places and failed approaches
are remembered for that search. Team assignments prefer separate areas and expire after 2 s without renewal.

Search duration is 5/10/14/18 s for Easy/Normal/Hard/Expert. Exhaustion discounts confidence once, rather than every
frame, and a fresh report at the same location must wait 3 s before another search. Repeated callouts cannot keep
restarting the deadline. A displaced observation can start a new search immediately. Reports include search plans
and visual inspections. These are bounded heuristic beliefs; learned policies and human imitation remain future work.

## Navigation

`NavBuilder.Build(collision, min, max, profile, GEs, activationMask)`:

- **Floors**: a pawn-width support probe traced down each column of a 32 uu grid, walkable at normal Z >= MINFLOORZ
  0.7. This finds the highest stair tread under the body; a separate centre probe rejects unsupported gaps. Body fit
  uses movement's collision shape, and queries use a pawn source so decorative meshes that do not block players
  cannot cut off a route. Nodes are flagged standing when the standing body fits too. After a surface, sampling
  clears the same pawn-width footprint before descending again; this prevents overlapping ledge and stair probes
  from producing repeated floors inside the same solid. Clearance steps cannot skip a usable crouch-height gap.
- **Walk and crouch edges** to the eight neighbours a swept body reaches with at most MAXSTEPHEIGHT 34 up, standing when
  both ends and the sweep fit a standing pawn, crouched otherwise (vents and crawl spaces; a crouched merc is 136 tall
  and fits fewer of them). A small step can leave a gap of two or three sampled cells because the lower cylinder
  overlaps its riser. Short connections across those gaps require continuous walkable floor at half-cell intervals
  and a clear body sweep, including equal-height endpoints around thresholds; they never bridge unsupported holes.
  Narrow stairs may span several treads between grid columns; each half-cell transition must remain within
  MAXSTEPHEIGHT, with support under the centre as well as the footprint. Immediate neighbours are connected first;
  longer edges are omitted only when a standing walk chain through the exact intermediate columns already reaches
  the target. Crouched chains still allow a faster standing shortcut. Navigation cache version 12 rebuilds older graphs.
- **Drops**: one way, up to three cells out, cleared all the way down; spies to 700 uu (a few points of damage past
  Fell_MinHeightForDmg 600), mercs to 400 (their damage starts at 450); deeper than the damage height costs extra.
  The landing floor must go on for two cells past the landing node in the direction of the drop: a narrow shelf over
  a deeper drop catches a walker by the arms or lets it fall on past, so it is no landing.
- **Traversal links**: ladders (LADDER) for both sides, up from the grab spot 35 uu out on the climber's side to the
  floor beyond the top, and down from the floor 72 uu back from the top. For spies also: ledges (LG) whose lip is 215
  to 320 uu above a floor with a wall under it, one link every 96 uu along the lip; vertical pipes (PIPE) from 100 uu
  out on the climber's side to the floor 60 to 120 uu beyond the top; zip lines (ZL) from a run-up 250 to 330 uu under
  the line near its high end to the floor under the low end; horizontal pipes (HOH) 190 to 360 uu above a floor, from
  80 uu in from one end (walked into along the bar, then a jump straight up), hand over hand to the far end and a
  drop; fences (FENCE) from the floor on one side, up, over the top and down to the floor on the other, at up to four
  spots along the fence. Only GEs active under the level's activation mask at the start take part. Poles get no links
  (no pole states, and no converted map has a POLE GE). Wall jumps, splits, back-to-wall gaps and barrier hops are not
  planned: they come from geometry, not GEs. Pipe-to-bar and bar-to-zip chains are not planned either.

Graphs are cached per map (name, sim hash, activation mask) and profile: in memory for the process and on disk
(`%LOCALAPPDATA%/SVM/ai-nav`, or `SVM_AI_NAV_CACHE`; empty disables it), never in the checkout. Cold builds take longer
than loading cached graphs. Local play prints `SVM_PLAY load seconds=... nav` progress for each side, including whether
the graph was built or cached and its node/edge counts, so startup work is visible before the match begins.
Node light levels (`ApplyLight`) let a route weigh the
dark: the stealth cost is `cost x (1 + 3 stealth x light)`, plus a danger cost near believed enemies.

Doors are planned as open. `DoorAi` (from `AiBrain.MoveTo`) looks about 220 uu along the route for a closed mover. It
waits for one that is opening and leaves bump-opened or trigger-opened doors to the walk. This includes
`SPresenceTrigger` sensors, such as Warehouse's merc spawn exits, when the sensor accepts the bot's pawn class.
A door opened by a switch the
bot's side can use, on its side of the door (a hack panel such as Cinema's), it opens as a player would: walk to the
use spot, hold Use until the menu opens, select the switch with S, let go, and keep off the keys through the hack. A
door nobody on the side can open, or one whose hack failed twice, is shut. A walker that sticks twice within 128 uu and
20 s does the same: the walk edges across a line in front of it (160 uu each way along a door's face, 64 otherwise)
are left out of the side's plans for 60 s, so later plans, even for a new goal, go another way.

An automatic ladder grab or a route change halfway along a ladder is recovered before replanning: climb toward the
goal's height, then plan again from a floor. A missing floor node in midair must not strand the controller on a rung.
Fence grabs and top crossings are likewise recovered before floor planning. The brain finishes an active traversal
before choosing a new tactical goal, so a decision cannot discard the climb halfway up a spawn fence.

When a complete route is unavailable, the follower can advance to the closest reachable point in the graph. It
reports failure there, rather than arrival or steering across the missing connection, so the brain can choose
another route or objective instead of remaining motionless at spawn.

`NavFollower` plays a path with keys: it faces the next waypoint and holds forward, passes waypoints early when the
next is in a clear straight line, crouches for crawl edges, stands and walks (not runs) off ledges for drops and lets
the keys go while falling, waits up to 4 s while a teammate is under the landing spot or under the edge (a body
falling on a pawn drop-attacks it), then plans another way down without the ground near him (or gives the goal up),
and plays each link
as the recorded moves do (docs/spikes/R4-traversal.md): run at the wall, stop, jump, hold forward to pull up; walk into
a pipe and hold forward; face down a zip line, wait out the 2 s special-move cooldown, run 0.2 s, jump, ride. A pawn
caught hanging from a ledge it did not plan climbs when the next waypoint is on top and lets go otherwise (back on the
stick, which OnLedge reads; the crouch key too). A pawn standing in the way (cylinders less than 40 uu apart along
the walk, overlapping its width) is stepped round with a side step away from his side, chosen once per pawn, for up
to 3 s; a waypoint he stands on is passed. A walker that makes no progress wiggles free (forward, a side step, a jump,
the jump left out within 90 uu of another pawn: it landed on his head), then plans around the place; a link that
fails is left out of the side's plans for 60 s.

## Behaviour

A utility choice every decision period (the difficulty's), with noise and a bonus for the current goal.

| Spy goal | When | What |
|---|---|---|
| Hack | a terminal is left | the cheapest terminal (distance plus known danger, teammates' claims pushed back); sneak there crouched (silent) by a dark route, or run when no merc is about; stand still, hold Use until the menu opens, release; wait while the terminal states hack |
| Evade | a merc it sees faces it while it is lit, or it was hurt | run to the best spot within 900 uu: out of the merc's line, dark, away (gadgets: see "Spy gadgets") |
| Hide | a merc nearby looks elsewhere and the spy is in the dark | crouch, freeze and watch, up to 5 s at a time |
| Shock | a merc within 550 uu facing the spy, any merc within 350 uu, one facing it at mid range while it is exposed, or one near the target terminal; the spy has the pistol and 15 energy; gadget use rolls for it | tap the fire-mode key (the evasion's silent long press unless the merc is closing in), wait for the armed stance (Fire is ignored during the draw), aim at the chest after the reaction time, Fire; one more shot on a miss; then the holster; the 4.5 s after a hit favour the hack |
| Grab | a merc seen in the last 1.5 s, on the spy's floor, turned away, alone (no other merc known within 1200 uu), not shocked in the last 5.5 s, and the spy in the dark or the merc near the target terminal or within 500 uu; melee use rolls for it | creep to a spot behind him (his yaw + 7200), walk the last steps standing, press Use when the controller's grab test passes; in the hold release Use and press it again (neck break), Fire (knock-out) after three tries |
| Drop | a merc standing still 120 to 650 uu below, on the landing of a drop edge of the spy's graph near the spy, not facing it, without sticky skin; melee use rolls for it | walk to the top of that drop, stand, walk off toward him, steer at him in the air (the drop attack knocks him down); a spy hanging from a ledge right above a merc lets go onto him |

A drawn shocker with no shock under way (a goal or team goal took over, a roll with the fire-mode key) is holstered
with one fire-mode tap at least a second apart; the gadget layer's sequences (heartbeat, aimed throw, cam) own the key
while they run. Spies roll (the roll key, unarmed, running) to dodge while evading or when seen or hurt on the way to a
terminal, and to go faster with no merc about, only along a straight open stretch of the route at least 450 (dodge) or
650 uu (speed) long, the view and the body lined up.

Spies switch night vision on in the dark and off in the light, and thermal near smoke.

| Merc goal | When | What |
|---|---|---|
| Finish | a spy it saw go down (lying, in sight or seen lying in the last 3 s) | run to him, face him, press Use when the game's finishing search would take him (FindPawnToReanimateOrTerminate: 250 uu, in front, feet within 10 uu, clear box to the spot 110 uu behind him), then Use again in the hold to kill at once; shoot him low (0.6 of his height under his centre) until then |
| Engage | a confirmed spy in sight | after the reaction time aim at the chest (leading by the lead factor) with an error that shrinks from the first-shot to the settled spread while tracking; fire short bursts, not through a teammate; side-step at range (hard and expert); reload on an empty clip |
| Respond | a terminal is being hacked (the nearest merc goes, with teamwork) | run to a spot 200 to 600 uu from the use spot that sees it, watching the spot |
| Frag | a spy lost behind cover 400 to 1600 uu away in the last 4 s | select the frag, aim a full-strength throw on a low arc, tap the throw key |
| Investigate | a visible shape, noise, callout, lost spy, or hurt by something unseen | inspect visible shapes without attacking; search reachable locations around lost contacts, retaining checked places and dividing areas with teammates |
| Guard | otherwise | a reachable standing post beside an untaken terminal no teammate holds, look round, change post every 12 to 30 s |

Mercs light the torch in the dark (on below light 0.15, off above 0.35) while responding, investigating or engaging
(and while guarding, by gadget use) and use motion tracking while investigating a noise in the dark (something moved
there) or on some dark posts; a terminal alert gets the torch, since a hacker stands still. A frag goes only at a
spy that was seen (by the merc or a teammate's callout) and lost, not at a noise.

At close quarters an engaging merc presses the Use key as a player does (spec/combat/melee-elbow-charge-berserk.md
rule 1), only when the controller's rules would take it: with a spy 140 to 550 uu ahead, lined up within 12 degrees,
on a clear line and with the charge and spin cooldowns met, forward and Use (the charge, which homes within the 0.8
cone); within 130 uu in front, out of the spy's wake protection and with no team mate within 220 uu, Use alone (the
berserk spin when ready, the elbow while it cools). Each chance is taken by MeleeUse, looked at once a second; hard
and expert mercs leave a spy holding his shocker alone (a charge into him shocks the merc). During a charge or a spin
the bot leaves the keys and the view to the state.

At point blank (under 110 uu), when the spy's body blocks the muzzle (the game's CheckIfCanFire), the strike is always
taken; without one the merc steps back to shoot. A charge is not started with a team mate within 120 uu of its line or
of the spy. A merc whose walk is stopped against a spy's body (a player sees his walk blocked) stops and looks at the
spot for 2 s; a spy who keeps still beside a still merc gives no cue.

### Spies and the motion tracker (`SpyEvasion`, Evasion.cs)

After the spy brain plans a tick, the evasion adjusts it from what the bot knows of the mercs (its memory, never a
merc's vision mode, which a player cannot see): a merc within the tracker's reach (2600 uu plus the spy's radius, plus
a margin by stealth) with an open line makes a walk a crouched creep at the slow speed (88.5 uu/s, under the 150
threshold; standing slow is 169 and is locked); on Hard and Expert a merc in sight that faces the spy within about
1760 (Hard) or 2000 uu (Expert) makes it keep still, and a draw of the gun is held past 0.5 s (no laser). A hurt spy runs. Easy
does not evade. The creep is kept for the dark: when the light on the spy, as a remembered merc would see it, is
above 0.25 (the level the hide choice calls dark; a map without light data reads 0.6 everywhere), the merc sees the
spy whatever its speed and the bot walks on at full speed (with creeping everywhere, Normal spies crossed TestBox at
88 uu/s under the guards' eyes and reached a terminal in 1 of 12 matches).

### Spy gadgets (`SpyGadgets`, SpyGadgets.cs)

After the spy brain (and before the evasion) the bot may use a gadget of its loadout, looked at every 0.25 s while it
walks free (not shocking, in a melee move, at the terminal or helping a mate). Everything goes through the keys:
GadgetQuickChoice with the Shocker holstered (the quick launch: 2 s apart, the grenade pops where it first lands at
the feet), SelectGadget, the fire-mode key held past 0.5 s to draw (no laser) and tapped to holster, AltFire tapped for
an aimed throw at full strength (2010 uu/s, falling at 750 uu/s^2: the bot aims the low arc) or to shoot a cam, Use for
the cam's gas. Charges and refire times are checked; a use counts when the gadget's LastUsed changes.

| Gadget | When | What |
|---|---|---|
| Flash-bang | a seen merc faces the spy 200 to 1800 uu away | holstered: quick launch at the feet (it flashes every merc within 2500 uu facing it with an open line); drawn: thrown at him (it pops on touch). He counts as blind for 4 s (`IsBlinded`) |
| Smoke | the spy was just hurt, or a facing merc is within 1100 uu; the last 450 uu to a terminal with a merc about (once per terminal) | quick launch at the feet (a 300 uu cloud for 10 s that blocks the merc's sight) |
| Smoke | a seen guard of the target terminal without a gas mask, within 1200 uu | thrown at him: he coughs, faints after 6 s inside |
| Chaff | a merc within 550 uu closing in | quick launch at the feet: within 300 uu his goggles go back to normal vision and his HUD is jammed while he stays; torches fail for 5 s |
| Chaff | a guard of the target terminal (believed within 800 uu of it), 300 to 1500 uu away, open line | thrown short of his feet (bursts at the 3 s fuse) |
| Heartbeat | on the way to a terminal (400 to 3500 uu out), or when no merc is known; no merc in sight | select the sensor, draw, sweep the view +-60 degrees toward the terminal (all round when nothing is known) for 1.6 to 2.4 s while walking on, holster. Each merc the radar shows (2200 uu, 30 degree half cone, through walls) goes into memory at confidence 0.8 (`AiContactSource.Sensed`), blurred by 3 % of its distance |
| Sticky cam | a guard standing still near the target terminal | a wall is searched (33 lines round the guard) where the cam's gas point lands within 90 uu of him: the cam faces the spy after the entry and the gas goes 192 uu along its view. Shoot it, watch through it (the bot's sight then comes from the cam), press Use when a merc is within 168 uu of the gas point with an open line, leave with Alt-fire 1.5 s later, when hurt, when a merc is believed within 600 uu of the spy's body, or after 7.5 to 15 s |

GadgetUse sets how readily each gadget is tried, the aim tolerance of a throw and the pause between uses; below 0.3
(Easy) only the quick launches are used.

### Helping team mates (`TeamAi.cs`)

Before the side's brain, every tick, the bot looks at its team mates (any player of its side, human or bot) and may
take a team goal instead; while one runs the brain waits, and it plans afresh after. The goals and their order of
worth (weighted by the teamwork knob, nearer mates first, none while a confirmed enemy is within 1500 uu, except a
revive; such an enemy also calls off a help already on the way, and a mate who lost health in the last 6 s is in a
fight and is not walked up to for a heal or unspy):

| Goal | When | What, through the keys |
|---|---|---|
| Revive | a mate lies knocked out (`bLyingOnGround`, `bSleeping`) within 2500 uu | walk to 110 uu of him, look at him, press Use (the revive search of the walking states) |
| Unspy | a mate carries a spy mark | stand in front of him, facing him (the menu's mate search wants both: 200 uu, 0.75 both ways), press Use, move the selection with S to Unspy Mate, release Use |
| Heal | a mate is under 80 % of his class health | the same with Heal Mate |
|  | (Unspy and Heal only for a mate on his feet: walking, not hanging, climbing or falling) | |
| Climb on a boost | a spy mate crouches ready to boost (`bReadyForShortLadder`) | walk in front of him, press the co-op key (T) when the mate search answers 1 (the HUD's "climb on him"), let the moves run, pull up |
| Climb on a hoist | a spy mate hangs ready to hoist | stand under him, facing the wall, press T when the search answers 3 |
| Offer a boost | a human spy mate stands still for 1 s within 260 uu of a boost spot, looking up at its ledge | walk to a spot of that ledge beside him, press T (the helper walks onto it and crouches); stand down after 20 s ready with nobody climbing |

The menu approach: the bot walks to a point 190 uu out along the line the mate faces (shorter when that line is
blocked), then straight at him so its body turns with the walk, stops 125 uu short (closer, a walk into him starts
the spy's vault), backs off when its body still points elsewhere, and presses Use in short presses (the walking
states act on the press). A goal with no route and no open line for 3 s, or unfinished after 25 s, is given up for
a while. A bot caught hanging from a ledge on the way keeps its route's goal, so a ledge of the route is pulled up
(steered at the mate himself it planned anew from the hang, whose first point was the floor below, and let go of
the ledge every time it caught it); off the route the walk climbs or lets go. The bots' choice of when to help is their own (the original has no bots); every move they make is the
original's (spec/objectives/team-interactions.md, spec/movement/coop.md, spec/combat/knockout-and-terminate.md).

Every Use press of a team goal is made only when the game's own search would take it, 25 uu inside its reach, with no
movement key on that tick: a merc's Use that finds nothing is the berserk spin or the elbow, and Use with forward is the
charge. A revive presses only while the mate still sleeps (one who is getting up, revived by this bot or another, ends
the goal) and within 225 uu in 3D (a mate lying lower or higher gets a step closer). A menu press waits until the bot
and the mate are nearly still, the bot is within 175 uu and both facings are above 0.8.

### Goggles, torch and the host's ability switches

- **Goggles**: the game spawns a player's SGoggle on the machine that draws (never on a dedicated host), so a server
  bot's controller gets one from `AiController` when its pawn arrives, as a listen host's own player has it. The mode
  starts at the goggle clip's notify (`AnimEvent_GoggleSwitchDown`, and ends at `AnimEvent_GoggleSwitchUp`); the port
  raises no animation notifies yet, so a bot's notify comes `AiK.GoggleClipSeconds` (0.5 s, not measured) after the
  key.
- **Ability switches**: the host's settings object starts from the original's `InitPersistentData`
  (`USPersistentData.Init`: night and thermal vision, grab, elbows, EMF and motion tracking, torch, laser, berserk and
  charge on, both lethal drops off; spec/equipment/loadouts.md rule 7), whether the play session installs it or the
  rules make it (`SGameInfo.PersistentData`). PostLogin copies the switches into the replicated game info at every
  login, bots included, and the controllers read them there. The bots play with the switches as the host has them.

## Difficulty

`AiDifficulty.For(AiSkill)`; every knob can be set on its own.

| Knob | Easy | Normal | Hard | Expert |
|---|---|---|---|---|
| ReactionTime (s) | 0.9 | 0.45 | 0.28 | 0.18 |
| AimErrorDegrees / AimSettledDegrees | 10 / 4 | 6 / 1.5 | 4 / 0.8 | 2.5 / 0.4 |
| AimSettleSeconds | 2 | 1.2 | 0.8 | 0.5 |
| TurnRateDegrees (per s) | 200 | 360 | 540 | 720 |
| LeadFactor | 0 | 0.5 | 0.8 | 1 |
| SightRange (uu) | 2000 | 3000 | 3500 | 4000 |
| LightThreshold | 0.3 | 0.18 | 0.12 | 0.08 |
| FocusHalfAngle (deg) / PeripheralFactor | 25 / 0.3 | 35 / 0.5 | 40 / 0.65 | 45 / 0.8 |
| SpotSeconds | 1.2 | 0.6 | 0.4 | 0.25 |
| HearingScale / SoundThreshold | 0.6 / 0.25 | 1 / 0.12 | 1.2 / 0.08 | 1.4 / 0.05 |
| MemoryHalfLife (s) | 3 | 6 | 10 | 15 |
| DecisionPeriod (s) / DecisionNoise | 0.5 / 0.35 | 0.25 / 0.15 | 0.2 / 0.08 | 0.15 / 0.03 |
| SearchSeconds | 5 | 10 | 14 | 18 |
| GadgetUse / Teamwork / Stealth | 0.2 / 0.1 / 0.2 | 0.6 / 0.5 / 0.6 | 0.85 / 0.8 / 0.85 | 1 / 1 / 1 |
| MeleeUse | 0.15 | 0.45 | 0.7 | 0.9 |

## Using it

- Offline play: `--bots=N` (up to 15) fills the other slots around the local player's side, `--difficulty=` picks the
  preset. The graphs are prepared before the host starts. With `--svm-host=inline` the play scene prints each bot's
  status every 10 s.
- Code: `new ServerBots(host, new ServerBotSettings { Bots = ServerBotSettings.Fill(3, humanTeam, difficulty) })`; the
  bots log in on the host's first tick (or `LoginAll()` before a listen host's own player starts the match).
- Headless: `new AiBotMatch(new AiBotMatchSettings { Map = "SteeE", Bots = ... }).Run(660f)` returns an
  `AiBotMatchReport` (winner, reason, round time, objectives, kills, what each bot did).
- Tests: `dotnet test --project tests/OpenScct.Game.Tests/OpenScct.Game.Tests.csproj -- --filter-namespace OpenScct.Game.Tests.AI`;
  SteeE's normal match runs when SteeE is converted, every difficulty with `SVM_AI_MATCHES=1`. Reports go to
  `local/test-results/ai/`.

## Historical matches (before the vision and search improvements)

These earlier Debug 2v2 results used equal difficulty and terminals that did not block players; they are not a
current balance benchmark. Current converted-map tests keep production terminal collision and report wall time.
The synthetic TestBox objective checks retain their explicit collision workaround because that fixture's
primitive terminal collider overlaps the use spot. Set `SVM_AI_MATCHES=1` for all difficulties and AquaE/ClubD matches.

| Map | Difficulty | End | Round | Kills (spies / mercs) | Merc bursts / frags |
|---|---|---|---|---|---|
| SteeE | easy | spies, END_STORY | 95 s | 0 / 0 | 1 / 0 |
| SteeE | normal | spies, END_STORY | 112 s | 0 / 0 | 6 / 0 |
| SteeE | hard | spies, END_STORY | 187 s | 0 / 3 | 26 / 1 |
| SteeE | expert | spies, END_STORY | 157 s | 0 / 4 | 26 / 3 |
| TestBox (no light data: every point lit) | normal | mercs, END_TEAMDEATH | 128 s | 0 / 8 | 92 / 1 |
| TestBox, mercs that neither see nor hear | normal | spies, END_STORY | 32 s | 0 / 0 | 0 / 0 |

Every SteeE round ends with the spies' fourth terminal. A SteeE match takes well under a minute of wall time in a
Debug build once the graphs are cached. The spies use ledges, drops and crawl spaces on the way (up to four
traversal links each). SteeE's darkness favours them: the mercs find them by the hack alerts, torches and motion
tracking, and the harder mercs kill more. The reports are in `local/test-results/ai/`.

## Deviations and open issues

- **Terminals**: players collide with the terminal mesh's simple collision (it ends 28.2 uu out, the per-poly
  triangles 33.2 uu), so a 35 uu pawn reaches the 64 uu use spot and terminals block players again in offline play
  with bots (`PlaySettings.TerminalsBlockPlayers` defaults to blocking; false still lets players walk through them).
  A spy stuck in that walk gives the terminal up after 8 s.
- **Spy melee**: the bots break necks (grab from behind, Use twice; or choke after 15 s), knock mercs down by dropping
  on them from drop edges or a ledge hang, and shock (stun). They grab standing: the port's grab from a crouch fell
  through to the elbow in a TestBox run. They do not elbow on purpose, and do not use the hoist ledge grab or drops
  from pipes and zip lines.
- **Sticky cam for server bots**: the smooth entry into a cam view ends in the camera code of the machine that draws
  it; a server bot draws none, so the bot changes to the view after the flight time. The bot cannot turn the cam (no
  mouse), so it places the cam where the cam's own facing puts the gas on the guard.
- **Map changes at run time**: the graphs use the GE activation mask at the start and the static geometry; doors are
  planned as open and handled on the way (`DoorAi`); other map events that open routes are not followed.
- **Pipes**: the movement port's pipe hold compares NewZipP with ZipP, which a plain grab does not set (see the
  traversal tests); a pipe link that fails is left out for a minute.
- **Torch placement** in the light rule is the merc's eye along its view (NativeTickTorche is not decoded; the
  renderer puts it on the `Laser` bone).
- **Light the bots do not see**: lights spawned by effects on clients (muzzle flashes, SFlashLight, flares, spy
  shock bullets: `EffectViews`), map lights riding movers or bones (`MoveLight`), spot shadow cookies (taken as 1)
  and glass: a shadow line through a window mesh with collision cuts the projector's patch that the screen draws.
- **HUD noise markers** (`ClientNotifyNoise`) are not read by the bots; the sounds behind them are.
- **A drop attack on a pawn hanging from a ledge** (fixed): `s_FallDropAttacked` plays SA_FALL_INERTE (106), whose
  falling physics and clip are the combat port's case; the walking-side fall group caught the action first, so the
  pawn stayed in PHYS_Ledge with no control for the rest of the match. The group now hands 106 (and the landings 92
  and 101) on (DropAttackedOnLedgeTests).
- **Balance**: search and vision have automated coverage, but perception thresholds, the motion blur factor and
  approximate prone samples still need visual gameplay calibration. Strategic patrols, coordinated ambushes,
  additional objective modes and training learned policies remain future work.
