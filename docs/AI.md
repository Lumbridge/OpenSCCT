# AI bots

The original game has no bots. SVM adds them so a player can fill a match alone and so matches can be played
headless for tests. The bots are meant to play fair: they act only through a player's input and perceive only what a
player could see or hear. This wave builds the foundations and a first competent spy and merc, with difficulty
presets.

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
| `src/OpenScct.Game/AI/AiLightField.cs` | the pawn lighting rule (zone ambient + four strongest lights) and the torch spot |
| `src/OpenScct.Game/AI/AiPerception.cs` | `AiSight`, `AiNoiseBoard` (MakeNoise hearing and sounds), `AiMemory` |
| `src/OpenScct.Game/AI/NavBuilder.cs`, `NavGraph.cs`, `NavFollower.cs` | navigation from collision and GEs, A*, following with keys |
| `src/OpenScct.Game/AI/AiBrain.cs`, `SpyAi.cs`, `MercAi.cs` | utility choice and the two sides' goals |
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

1. On screen: within the horizontal FovAngle of the controller's view (plus 4 degrees). Inside the difficulty's
   focus angle a target counts fully; outside it (peripheral) range and strength are scaled by the peripheral
   factor.
2. In range: the difficulty's sight range (thermal: at most 5000 uu, its fade depth).
3. A line: from the eye (location + 60 standing, + 30 crouched) to three points up the target's body (head, chest,
   feet) through level geometry and movers; the share that is open scales the strength. Smoke clouds (a 300 uu
   sphere round each `SSmokeEmitter`) block normal, night and motion vision; thermal sees through at 0.8; EMF draws no
   particles.
4. Lit enough, by vision mode:

| Mode (RendMap) | Shows the target when |
|---|---|
| Normal (5) | the light on the side of the target facing the observer is above the threshold for the distance: `LightThreshold x (0.5 + d / 1000)`; a running target shows 1.5 times better; within 100 uu a dark shape shows anyway |
| Night (34) | as normal with the light boosted: `(L + 1.4 x 30 / 255) x 4` (the mode's boost and post gain) |
| Thermal (36) | always (pawns are hot), strength `1 - d / 5000` |
| Motion (37) | never by sight: only while the bot's own goggle tracker holds a lock on the target and the target is within the view (`AiMotionSense`, the game's scan: 150 uu/s, 2600 uu plus the radius, an open line, no cloaked spy; spec/equipment/motion-tracking.md); a lock out of view is only the detect sound (`AiMotionSense.LastBeep`) |
| EMF (38) | a spy only with its electronics on (`PawnVision.IsSpyEmfVisible`: weapon out, goggles on, binoculars, spy bullet, cloak, phosphorus); mercs always |

A cloaked spy (`GlassEffect`) is up to 85 % harder to see in the light-based modes. Each look feeds a spotting meter
that fills in `SpotSeconds / strength`; at 1 the contact is confirmed and its position known exactly.

### Light (`AiLightField`)

The rule the renderer lights pawns with (docs/RENDERING.md, "Actor lighting"), Godot-free: the zone ambient plus the
four strongest lights (`bApplyToInGameLighting`, on, not ESpotShadow; own-zone lights only in their zone), ranked by
`brightness x (1 - d^2 / (R + r)^2)` with suns first and spots outside their cone at 0, each adding
`k C S(d / R) max(0, N.L)` (k 2, suns 1.75; C the FGetHSV colour; R = 25 (LightRadius + 1); S the smoothstep), capped
at 2; the level is the luminance. N faces the observer, so a spy lit from behind stays dark to a merc in front. A
merc torch that is on adds `colour x 2 x (1 - d / 1500)` inside its 400 x 400 spot at 1500 uu when the line from the
torch is open (the spot shadow light; the 2x gain is estimated as in the renderer). The lights come from the map's
`render/lighting.json`; a server with only the sim part reads every point as 0.6 (`AiLightField.Unknown`).

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

## Navigation

`NavBuilder.Build(collision, min, max, profile, GEs, activationMask)`:

- **Floors**: a 4 x 4 x 2 box traced down each column of a 32 uu grid (as the walking floor checks), walkable at
  normal Z >= MINFLOORZ 0.7; a node where the crouched body fits, flagged standing when the standing body fits too.
- **Walk and crouch edges** to the eight neighbours a swept body reaches with at most MAXSTEPHEIGHT 34 up, standing when
  both ends and the sweep fit a standing pawn, crouched otherwise (vents and crawl spaces; a crouched merc is 136 tall
  and fits fewer of them).
- **Drops**: one way, up to three cells out, cleared all the way down; spies to 700 uu (a few points of damage past
  Fell_MinHeightForDmg 600), mercs to 400 (their damage starts at 450); deeper than the damage height costs extra.
  The landing floor must go on for two cells past the landing node in the direction of the drop: a narrow shelf over
  a deeper drop catches a walker by the arms or lets it fall on past, so it is no landing.
- **Traversal links** (spies): ledges (LG) whose lip is 215 to 320 uu above a floor with a wall under it, one link
  every 96 uu along the lip; vertical pipes (PIPE) from 100 uu out on the climber's side to the floor 60 to 120 uu
  beyond the top; zip lines (ZL) from a run-up 250 to 330 uu under the line near its high end to the floor under the
  low end. Only GEs active under the level's activation mask at the start take part. Ladders, fences, poles and
  hand-over-hand bars get no links while the movement port has no states for them.

Graphs are cached per map (name, sim hash, activation mask) and profile: in memory for the process and on disk
(`%LOCALAPPDATA%/SVM/ai-nav`, or `SVM_AI_NAV_CACHE`; empty disables it), never in the checkout. SteeE builds in a few
seconds in a Debug build and loads from the cache in a moment. Node light levels (`ApplyLight`) let a route weigh the
dark: the stealth cost is `cost x (1 + 3 stealth x light)`, plus a danger cost near believed enemies.

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
| Evade | a merc it sees faces it while it is lit, or it was hurt | run to the best spot within 900 uu: out of the merc's line, dark, away; a quick-launched smoke at the feet when the merc is close |
| Hide | a merc nearby looks elsewhere and the spy is in the dark | crouch, freeze and watch, up to 5 s at a time |
| Shock | a merc very close, or guarding the target terminal with its back turned | draw the shocker, aim, fire, holster; the stun window is the moment to hack |

Spies switch night vision on in the dark and off in the light, and thermal near smoke.

| Merc goal | When | What |
|---|---|---|
| Engage | a confirmed spy in sight | after the reaction time aim at the chest (leading by the lead factor) with an error that shrinks from the first-shot to the settled spread while tracking; fire short bursts, not through a teammate; side-step at range (hard and expert); reload on an empty clip |
| Respond | a terminal is being hacked (the nearest merc goes, with teamwork) | run to a spot 200 to 600 uu from the use spot that sees it, watching the spot |
| Frag | a spy lost behind cover 400 to 1600 uu away in the last 4 s | select the frag, aim a full-strength throw on a low arc, tap the throw key |
| Investigate | a noise, a callout, a lost spy, or hurt by something unseen | go there watching the place, look round, give up after 4 s |
| Guard | otherwise | a post beside an untaken terminal no teammate holds, look round, change post every 12 to 30 s |

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

## Matches (Debug build, 2v2, both sides at the same difficulty, terminals not blocking players)

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
- **Spies cannot kill yet**: grab, neck snap, drop attack and elbow are not used; the shocker only stuns. Spies win by
  objectives or the clock.
- **Map changes at run time**: the graphs use the GE activation mask at the start and the static geometry; doors are
  taken as open, map events that open routes are not followed (map-specific, later).
- **Pipes**: the movement port's pipe hold compares NewZipP with ZipP, which a plain grab does not set (see the
  traversal tests); a pipe link that fails is left out for a minute.
- **Torch placement** in the light rule is the merc's eye along its view (NativeTickTorche is not decoded).
- **HUD noise markers** (`ClientNotifyNoise`) are not read by the bots; the sounds behind them are.
- **A drop attack on a pawn hanging from a ledge** (fixed): `s_FallDropAttacked` plays SA_FALL_INERTE (106), whose
  falling physics and clip are the combat port's case; the walking-side fall group caught the action first, so the
  pawn stayed in PHYS_Ledge with no control for the rest of the match. The group now hands 106 (and the landings 92
  and 101) on (DropAttackedOnLedgeTests).
- **Balance**: mercs need better search (flashlight sweeps, patrol routes through dark rooms) and ambushes; spies need
  melee kills and team tactics. These are the next steps of the plan ("AI bots").
