# Social hub

[Documentation](README.md)

The hub is a shared space where spies and mercenaries can chat, queue for matches and use practice activities.
It supports proximity voice, match pads, three assault courses, an aim range, emotes and a jukebox.
The design targets about 50 players; the [recorded soak test](#the-soak-test) describes the test conditions.
The hub is an OpenSCCT addition, with rules in [the hub specification](../spec/additions/hub.md).

## Visit a hub

Open **Hub** from the main menu, select a listed hub and join. You can also join by address or host your own.
On a match pad, use its laptop to choose the match settings, then ready up. The hub returns you to the pads
after the match. A hub hosted in the game closes when its host leaves.

| Default key | Action |
| --- | --- |
| J / K | Play as spy / mercenary |
| N | Return to the plaza |
| F4 / Travel button | Travel to any district |
| U | Use a laptop or activity; ready on a pad |
| H | Watch the pad's match |
| X | Stop an activity |
| G | Choose an emote |
| P | Open the player list, voice volume and mute controls |
| Enter or F5 / F6 | Chat to everyone / your side |
| Tab while typing | Cycle everyone, team and nearby chat |
| Escape | Open the menu to leave |

All Hub shortcuts, including individual emote choices, are available in **Options → Controls → Hub** for
keyboard/mouse and controller assignment. Chat and menu commands remain in their existing categories. HUD hints
show the saved keys; removing a binding stays removed after reload. U is the default ready/use key and does not
open travel, including at stations. The Travel button remains available when its shortcut is unbound.

Voice is heard by nearby players. See [proximity voice](#proximity-voice) and the [voice setup guide](VOICE.md).
Server owners can jump to [Running a hub](#running-a-hub) or [Match pads](#match-pads-h2).
The remaining sections cover the map, implementation and validation.

## Pieces

| Location | Responsibility |
| --- | --- |
| [tools/hubmap](../tools/hubmap/) | Map layout, procedural art and generator |
| [fixtures/hub](../fixtures/hub/), [fixtures/hubmap](../fixtures/hubmap/) | Generated map, area manifest and match-pad configuration |
| [OpenScct.Server/Hub](../src/OpenScct.Server/Hub/) | Sessions, match pads, activities, chat, moderation and scores |
| [game/Scripts/Hub](../game/Scripts/Hub/) | Hub screens, player list, signs, activities, themes and audio |
| [Voice/Proximity](../src/OpenScct.Net/Voice/Proximity/) | Positional voice, occlusion, volume and mute |
| [Hub tests](../tests/OpenScct.Integration.Tests/Hub/) | Session, match-pad and activity checks |

## Running a hub

```
dotnet run --project src/OpenScct.Server.Cli -c Release -- --hub [--port 28886] [--max 50] [--name "My hub"] [--motd "Hello"]
    [--master https://master.example.org --key <invite key> [--server-key <trusted key>] [--region EU] [--unlisted]]
svm-server --hub --config svm-hub.json     # name, port, maxPlayers, motd, maps and master (with region) from the file
```

A hub listens on UDP 28886 unless `--port` or its config file names another (`HubK.DefaultPort`): its own port, so
one PC can host a hub and a game (28888) at once. `svm-server --hub` keeps its bans in `hub-bans.json` next to the
server, beside `hub-scores.json`.

**Listing.** A hub with a master heartbeats like a game server (`MasterLink`, `HubServer.Info`), with `kind: hub`
and its `region`. The master lists hubs apart: `GET /api/v1/servers?kind=hub`; a list asked without `kind` (every
older game) gets game servers only. A hub answers server queries (the ping) and the master's reachability probes on
its game socket, so the master can say whether players reach it directly. The game's server browser skips a saved
address that turns out to be a hub (`ServerInfo.IsHub`, flag bit 4).

**Official hubs.** A dedicated hub with a trusted-server key (`serverKey`, in the master's `TrustedServers`) is
"Official" in the list, and official hubs come first. Each one names its region (`EU` now; a US hub later is a second
`svm-server --hub` on a US machine with `"region": "US"` and its own trusted key), and the list shows each hub's ping,
so a player picks the nearest. Players' own hubs are listed after them.

In the game: main menu, Hub (a menu page like the others: "Your visit" with a name and the side to arrive as, "Hubs"
with the master's hub list (name, region, Official / Dedicated / Player, players, ping; official first, then lowest
ping), "Join a hub by address", "Host a hub" with "Show it in the hub list"; Refresh asks the master again), then either

- **Join from the list**: pick a hub and Join (or double-click). The game asks the master the way in
  (`HubService.JoinListed`) and tries, in turn, the host's own network address (same router), its public address and
  the master's relay (a NAT punch is not tried; the relay covers those hosts). Pad matches go to the hub's public
  address; back from a match the game asks the master again.

- **Host**: the game opens a hub itself (`HubHost`: the dedicated server's `HubServer` on a thread of
  the game, UDP port 28886 on every interface) and joins it on 127.0.0.1. With a master set and "Show it in the hub
  list" on (`[hub] listed` in online.cfg, on by default) it is listed as a player hub; others join it from the list
  (through the relay when this PC cannot be reached) or by this PC's address (LAN IP, or the public IP with UDP 28886
  forwarded; Windows asks once to let OpenSCCT through its firewall). A listed hub's pad matches register on the
  master too (unlisted), so they are reached the same ways, the relay included. The hub closes when the host leaves it
  (Escape menu), which drops everyone in it.
- **Join by address:** enter the LAN or public address; the default port is 28886.
  Names appear above players in their team's colour. Converted character content is needed to draw pawns.
  Use the controls listed above; voice settings are shared with online matches.

The map is regenerated with `python -B tools/hubmap/generate.py` (and checked with `--check`; `python -B -m pytest
tools/hubmap -q`). Nothing in it comes from the game.

## The map

A 49,152 × 49,152 uu outdoor world, with 16,384 uu of vertical space, a photographic daylight skybox,
lawns, dense pine and birch groves, three shallow lakes and a stream footbridge. The enclosing collision boundary is invisible;
buildings and covered passages belong to individual districts. Its footprint is sixteen times layout v3.
Gameplay geometry is authored by `layout.py`, `expansion.py`, `landscape.py` and `courses.py`; `scenery.py` places the outdoor models
and `foliage.py` plants the soft ground cover.
Trees, flowers, the house and well come from the linked GamesNotDeveloped forest demo, with individual CC BY 4.0
author credits. Poly Haven supplies CC0 benches, sofas, tables, street lamps, rocks, crates, barrels, scanned surface
textures and the photographic sky. Sources, prepared hashes, licenses and modifications are tracked in
[`fixtures/hub-assets/CREDITS.txt`](../fixtures/hub-assets/CREDITS.txt) and copied into the map's `render/scenery/`.
`fetch_assets.py` refreshes this source cache explicitly; ordinary generation is offline and deterministic.
Install `tools/hubmap/requirements.txt` for generation/checks. Refreshing source models additionally uses
`tools/hubmap/requirements-assets.txt`; `prepare_collision.py` then rebuilds the tracked collision cache.
Original signs use JetBrains Mono Bold (SIL OFL 1.1). No Ubisoft assets are copied. Hard Jump 5.1 remains reference only.

- **Outdoor grounds**: open horizons, grass between paved district paths, warm daylight ambient and distant haze.
- **Woodland and lakes**: 805 trees across irregular groves and shore plantings, with clear walking corridors and
  arrival spaces. Three lakes have actual carved, sloping collision beds, shallow water and solid piers. Their
  surfaces are water effects, not invisible walkable floors. Timber edges retain the same GE coverage checks.
- **Ground cover**: 267,374 grass clumps and 1,756 ferns fill the woodland floor, meadows and lake banks. Shorter
  grass surrounds the plaza; paved paths, arrivals, start gates, recovery terraces and water stay clear. The grass
  texture comes from the forest demo's MIT-licensed SimpleGrassTextured assets; the four fern variants and leafy
  terrain photograph are CC0 Poly Haven assets. The source photograph remains intact; baked terrain colour blends
  with the vegetation at distance. Original wind shading anchors the plants at their roots. Each 2,048 uu cell
  uses MultiMesh batches, shared meshes/materials and bounded culling, with gradual distance fading. These soft
  fronds do not obstruct movement; solid terrain, props and tree trunks retain their collision and usable GEs.
- **Scenery**: textured birch and pine groves, flower beds, scanned rock outcrops, houses, a well, picnic tables and
  street lamps replace placeholder dressing. Every rigid imported instance has collision derived from its transformed
  model, including furniture legs, walls, roofs, trunks and branches. Leaf cards stay soft. Explicit source pivots
  and scales are shared by rendering and collision so rotated props do not retain their old blockout boxes.
  Imported albedo and normal maps use display-space shading and the existing per-camera fog and vision globals.
- **Plaza** (spawn): granite pavers with the OpenSCCT ring inlaid round the fountain (water in the basin, neon on its
  lip and up the column), a four-sided "OPENSCCT HUB" sign hung over the lookout, four neon pylons (green west, amber
  east), eight benches, a SPIES banner on the west side and a MERCS banner on the east, and floor arrows to every area.
  An open timber pavilion surrounds the fountain and supports the suspended signs. Other district titles use
  grounded timber frames. The plaza has one travel directory; duplicate entrance/leaderboard title panels are removed.
- **Garden** (traversal): the station kit on striped turf, a label on every station (block faces and turf paint),
  ladder rungs, zip cables, six trees (trunks collide, canopies are above any jump), a "TRAVERSAL GARDEN" wall sign.
- **Match pads**: four 1024 courts, each railed (16 thick, 96 high, vaultable LGs on both faces, a 256 opening in the
  middle of every side) with 64 corner posts, a sign gantry on its north side (a board with "PAD n / next match"
  facing south and north), wooden bleachers with amber step lights north of Pad3 and Pad4 and east of Pad2 and Pad4,
  a hanging "MATCH PADS" sign facing the plaza, and a theme kit per pad (below).
- **Aim range**: a wooden booth with a counter on the firing line (LGs on both faces) and five numbered stalls
  under a canopy with amber neon, painted targets on the berm, distance marks every 512 uu (5 M to 20 M), a
  leaderboard board at the north end facing the plaza.
- **Easy — Training Grounds**: the original course translated south with its obstacle dimensions and order preserved:
  climb, fence, hand over hand, shimmy, split jump chimney, wall jump and zip line to the finish.
  A start line, a "MOVEMENT COURSE" sign over the route and a leaderboard board near the start facing the plaza.
- **Lounge** (new area, south-west): a carpet, two sofas with coffee tables and two side sofas facing a big screen on
  the west wall with a scrolling ticker, a bar with stools and magenta neon, planters, warm light, "LOUNGE" signs.
- **District landmarks**: a water tower with "OPENSCCT" on it and a mast, air-conditioning units, a helipad
  (south-east).

### Courses and travel

| Stable course ID | Theme | Sections | Target clean run | Attempt limit |
| --- | --- | --- | --- | --- |
| `easy` | Training Grounds, original route | Original checkpoints | Original course | 5 minutes |
| `medium` | Overgrown Ruins: aqueduct, ledge gallery, service courtyard, canopy lift, ramparts and tower extraction | 6 | 2.5–4 minutes | 45 minutes |
| `hard` | Frozen Observatory: razor approach, split shafts, frozen gallery, shuttle, utility spine, vaulted passages, high lift and summit | 8 | 4–6 minutes | 90 minutes |

The course-design report supplied on 2026-11-06 informed revision 2 of Medium and Hard. The user selected its shorter
clean-run targets. `courses.py` replaces the repeated stepping-stone rings with connected architectural galleries:
calibrated run-jumps, rail vaults, roll-under arches, ledge catches, long shimmies, pipes, ladders, hand-over-hand
crossings, fence transfers and zip-line extraction. Hard combines these transitions and narrows the landing lanes.
Its 408-uu faces support contextual wall kicks, with 192-uu side-wall chambers retaining the native split-jump space.
The existing three replicated movers remain shared timing challenges. Broad belvederes provide section recovery,
views and optional exploration space. Invisible validation gates stay separate from the visible section markers.
Native collision probes determine the actual dimensions: for example, the roll arches clear the standing roll's
initiation probe, and pipe GEs sit on the physical pipe's outer surface so the top-exit sweep is unobstructed.
Fence tops retain their full-span FENCE GE; adding a nearer LG there would mask the native climb-over transition.
The authored `playtestRoute` metadata describes normal-control verification, and is not displayed as route arrows.

The two new courses use ordinary spy movement and normal friction, including on ice. Their ordered validation
gates are invisible and vertically bounded; only section checkpoints are marked. Falls restore the current
section's gate progress and safe landing while time continues. A fall before the first checkpoint returns to the
start with the same timer. Progress ends with the attempt. Each course has its own personal bests, top-ten board
and revision. The legacy reader migrates layout-v3 scores when Easy retains revision 3. The final traversal coverage
pass adds perimeter grabs and advances Easy to revision 4, so those old times are not mixed with the changed route.
Changing another course's revision clears only that course's table.

The rebindable Travel action (default F4) or HUD button opens the eight-destination directory anywhere in the hub.
U remains ready/use, including beside stations. Every destination is immediately available. Selecting one cancels an activity and leaves the match queue
only after the server finds a collision-free arrival. A busy destination leaves the attempt intact. Successful
travel clears movement, traversal attachment, readiness and prediction history, applies the destination facing,
and starts a two-second cooldown. Arrival decks are outside course gates and match queues. Recovery uses the same
placement path and tries additional positions on a crowded checkpoint deck.
Course stations face the nearby start ramps. Entrance titles sit at the actual start approach, with mounted
leaderboards on adjacent terraces; Easy's title and board have also been brought to its original start.

Static render meshes are split into spatial cells and grouped by material within each cell. Course movers use
native `Engine.Mover` interpolation with local collision hulls and matching render instances. The server refreshes
the three movers' interpolation phase for late arrivals. Within a leg, Hub clients correct speed gradually instead
of restarting interpolation at each rounded, delayed snapshot. Collision, based riders and attached GEs use the
same continuous motion. First snapshots and new legs still use native mover interpolation. Exact per-leg timings
in `sim/movers.json` avoid rate rounding on the wire; ordinary map movers retain their native receive behavior.
Medium's Canopy Lift and Hard's High Lift travel each way in 3.5 seconds (twice their former speed), with the same
three-second boarding stops. Hard's horizontal shuttle retains its eight-second travel time.
The finish circle accepts any approach across the final open terrace once the last obstacle is validated; the
normal-input test's final walking waypoints are no longer extra hidden scoring gates. Overlapping ordered gates
advance in one activity tick. Medium uses score revision 4, Hard revision 3, and Easy remains revision 4.
The 2026-11-07 Medium change passed all 24 generator checks, the Canopy Lift section and full Medium normal-input runs,
and both network travel/late-join mover checks. The revised full Medium run took 185.2 seconds.
The subsequent 2026-11-07 course fixes passed 24 generator checks, 12 Hub runtime cases, all 16 normal-input
course runs, six additional platform boarding/riding/exit cases (perfect transport and 60 ms ping/1% loss),
and 15 mover network/prediction cases including ordinary map doors. The full Hard run took 292.0 seconds.
Before the fix, both finish-edge regression cases failed and network refreshes moved platforms backward by
15.752 uu. After the fix, 30-second samples of all three movers showed zero backward motion at 0/60/120 ms
ping, including jitter, duplication, reordering and up to 2% loss. Largest per-tick travel was 4.572 uu and
server/client separation stayed below 52 uu. Stationary rider drift relative to the platforms was below .002 uu.
The windowed Hub smoke also passed travel, rebound controls, checkpoint pivots, team changes, chat and voice.
Every moving platform also has four full-span ledge GEs attached to the mover. Their world query positions and
the hanging/climbing pawn's anchors follow the carrier; jumping, falling, recovery and travel release it.
Static exposed usable edges receive full-span GEs, split only by physical obstructions. Trees and street lamps
have native PIPE climbs ending before obstructing branches. Tiny surfaces below the native grab span and soft
foliage are not assigned unusable ledges. `contacts.py` and its coverage tests guard subsequent additions.
`areas.json` keeps its legacy format reader and adds course IDs, revisions, sections, failure volumes and travel
destinations. Scores write `openscct.hubscores/2`, while reading the old singleton score file. Hub protocol 7 adds
course, section, personal-best and travel metadata. Client, server and generated assets should be delivered together.
Long leaderboards are split between reliable `board`/`boardadd` messages; the HUD and world signs use the complete
reassembled rows, so long player names do not remove entries from the top ten.

Local checks are `python -B -m pytest tools/hubmap -q`, the `ExpandedHubTests` and `ExpandedHubTraversalTests`
integration suites, the existing hub regression/soak suites, and `godot-smoke.ps1 -Tests hub` (which tours all eight
destinations). `SVM_HUB_TRAVERSAL_REPORT` records automated normal-input traversal times. The standard crowd check
uses 16 clients across the eight districts. Set `SVM_HUB_SCALE_REPORT` to a report path to run the 50-client
benchmark and record concentrated and spread measurements. `SVM_HUB_BASELINE_ROOT` points at a layout-v3 fixture root
for a 50-client comparison. `SVM_HUB_RENDER_REPORT` opts the windowed Hub smoke into a 50-visitor renderer
capture (crowd, spread plaza, spread observatory); `SVM_CONTENT` can point at a content root containing the baseline
map under `svmap/` for the old-map comparison. Automated controls establish movement reachability. Human playtests
cover route discovery, camera behavior and readability.

#### Outdoor dressing and controls follow-up, 2026-11-06

- Current generated map: **1,164 imported instances**, including **805 climbable trees**, three carved lakes,
  the open plaza pavilion, supported district signs and **4,726 traversal GEs**. Old planting inside the new
  lakes was relocated onto their banks. The generator checks the actual BSP lake openings as well as their bounds.
- **24 generator tests**, **117 input/binding logic tests**, **16 native route tests** and **8 expanded Hub runtime
  tests** passed. The latter includes U at stations doing nothing, followed by ready/unready on a match pad.
- The windowed Hub smoke passed the rebound keyboard/mouse/controller actions, modal close bindings, emote
  selection while gameplay is paused, eight travel arrivals, three stationary checkpoint pulses, chat/voice and
  side switching. The controller-defaults smoke also passed. Build: zero warnings/errors.
- The ground-cover follow-up adds 269,130 soft plants in 511 spatial cells. All 25 source-asset hashes match the
  recorded lock; the generated simulation hash remains `c45a1075e7bd058e667d5cb06d43cd58100bac498a31b0e3e031dd44cc5202d8`.
  The windowed smoke additionally checks foliage presence and culling bounds, and the refreshed standalone local
  preview passed its startup smoke. Eight mapshot views were inspected, including the woodland at player height.
- The first foliage crowd-render attempt timed out after the real client disconnected during visitor map loads.
  The benchmark held the render thread behind the visitor update lock. Visitor registration is now nonblocking,
  joins wait asynchronously for the preceding visitor to load, and visitor travel runs on its owning worker.
  Its setup budget is ten minutes; the ordinary two-client smoke keeps its existing limit.
- The corrected **50-player renderer capture passed** at **1280×720**: frame p50/p99 was **4.58/6.83 ms**
  concentrated, **3.87/5.48 ms** spread at the plaza and **3.79/5.50 ms** spread at the observatory. Draw calls
  were 711/667/392; video memory was 2,372 MiB and initial local load 12.03 s. The preceding outdoor map without
  ground cover measured 4.56/14.07, 4.08/10.02 and 3.49/6.82 ms, 676/631/385 draws and 2,351 MiB video memory.
  These are local observations with variable CPU/GC conditions, not a claim of improved performance. Managed
  memory was 24,366–24,545 MiB for the server plus 50 complete client simulations, not the dedicated server alone.
- The 50-client simulation check passed before the render-only foliage update: server ticks p50/p99 were
  3.25/28.81 ms concentrated and 3.25/20.74 ms spread. The unchanged simulation hash makes that result applicable
  to the final foliage assets; no course score revision or movement dimensions changed in this pass.
- Course arrival signage is now beside the real start. Duplicate title panels and the overlapping plaza directory
  were removed; score boards are mounted with a fixed facing. The final native runs still satisfy the report's
  clean-run targets. Human route-discovery, camera and visual review remain part of local playtesting.

#### Report-based course validation, 2026-11-06

- All 14 sections and both continuous courses completed with normal input over the client/server simulation,
  using the converted character and root-motion clips. Medium took **184.7 seconds (3m05s)** across 56 route
  actions; Hard took **294.5 seconds (4m55s)** across 75. Full-run tests assert the selected 150–240 and
  240–360 second ranges. These are scripted clean runs; first-time route discovery still needs human playtesting.
- Generator checks: **21 passed**, including material chunk bounds, clear gates/recovery/arrivals, mover
  collision, every rigid imported instance, full usable edge coverage and varied mechanics in every section.
- The **51 Hub cases** were verified across the full suite and focused reruns. The recovery fixture was updated
  to fall beside the raised start apron rather than request placement inside its solid volume. Seven expanded
  Hub runtime cases passed again after moving the new-course stations to their entrance ramps. The baseline
  benchmark now asserts that it actually loaded the one-course legacy map, preventing a silent fallback.
- The windowed Hub smoke passed all eight travel destinations, all three stationary checkpoint-pulse checks,
  chat, voice relay and the spy-to-merc change. The Godot solution built with zero warnings or errors.
- With 50 complete clients in the same process as the server, the revised map measured server tick p50/p99 of
  **3.14/23.32 ms concentrated** and **3.04/20.02 ms spread**; managed memory deltas were 7,203/7,383 MiB.
  A separate legacy-map rerun measured 5.74/64.58 ms and 1,816 MiB. Local CPU/cache conditions varied;
  these are observations, not evidence that the expansion improves performance or dedicated-server memory figures.
- The headless play loader now ignores the repository's `shared/ProjectLinks.cs` source directory when locating
  game content. Previously it could hide the real character clips and use placeholder animation timings.
  Native route playthroughs require converted clips and explicitly skip when that content is unavailable.

#### Initial expansion validation, 2026-11-06 (before the report-based route revision)

- Generator and collision checks: 20 passed, including deterministic tracked output, all three courses,
  spatial render bounds, clear arrivals, mover hulls, imported collision and full-span usable ledges.
- Hub integration: 49 passed, followed by two native-input zip-line regression cases after moving their
  newly physical supports out of the riding lane. Coverage includes complete Medium and Hard attempts,
  independent runners, recovery and gate rewind, long attempts, travel under packet loss, crowded arrivals,
  cancellation, score migration, queues, chat, voice relay and 50-player sessions.
- Physics: 211 passed; content: 162 passed. The Godot solution built with zero warnings or errors.
- A reported orange-checkpoint animation bug was traced to scaling world-offset children around the map origin.
  Marker roots now sit at their own checkpoint positions. The windowed smoke exercises the minimum and maximum
  pulse on all three courses and checks that each ring's centre stays fixed. Travel/recovery also applies the
  destination rotation to the owning client's view and the merc's desired aim, not only to the pawn mesh.
- Automated normal controls completed Medium in 690.1 seconds (11m30s) and Hard in 1342.6 seconds (22m23s).
  These establish reachability and a clean-run timing baseline; they do not measure a new player's route discovery.
- Native traversal checks cover all three movers with perfect transport and with 60 ms latency plus packet loss,
  duplication and reordering, all three tree models, street lamps, both original zip lines and imported ledge support.
  The generated map has 319 imported instances, including 116 climbable trees, and 10,991 ledge GEs.
- The final 50-client simulation measured median server ticks of 3.03 ms on layout v3 and 3.32 ms on the expanded
  map with a concentrated crowd; spread players measured 3.13 ms. The corresponding p99 values were 24.24,
  30.91 and 20.72 ms. These are local measurements, not a production capacity guarantee.
  Managed memory deltas were 1,820 MiB for the baseline and 7,954–8,145 MiB for the expansion with **one server
  and 50 complete client worlds in the same test process**; these are not dedicated-server memory figures.
  Cold/cache state affected map loading (1.5–7.3 seconds in that run), so load timings need a controlled hardware run.

Human assessment of route discovery, camera clearance, scenery quality and the full original Easy moveset remains
part of playtesting. Live public connectivity and two-person microphone checks are separate from local loopback tests.

**Collision and ledges.** `contacts.py` completes physical coverage after layout and scenery placement. Authored
boxes and wedges form the world BSP; uncovered beams, cables, rungs and signs receive spatial mesh collision.
Imported props receive simplified mesh collision in precisely the render transform. Physical model bounds and
collision source hashes are checked during generation. The goal crossbars stand above both teams' heads and
collide; the jukebox, desks and laptop lids also have simulation geometry. Decals, light effects and leaf cards
remain surface effects. All theme variants share their lantern silhouette and collision.

Usable rectangular edges receive full-span LGs. Physical intersections split a span; native clearance checks
reject edges with no hanging or pull-up space. Imported ledges follow the model's upward surface boundaries,
including holes and irregular outlines, and require a consistent clear approach along the usable span. They are checked
with native-sized hanging, raised and pull-up boxes. High roof rims can support a hang/shimmy even where the roof
blocks a pull-up; low edges require a usable vault/front-column approach and clear standing or crouched top.
Runtime collision traces additionally verify support beneath imported ledges. Trees and lamps receive PIPEs
fitted to the solid trunk, with their climb height capped before obstructing branches. Movers have four LGs in
actor-local coordinates. Existing garden ladders, wall jumps, split jumps, hand-over-hand bars and zip lines remain.

### Anchors for the other hub features (fixtures/hubmap/areas.json)

| Field | Where | What |
|---|---|---|
| `pads[].signAnchor`, `signYaw`, `signSize` | MatchPads | the centre of the gantry board's south face (2 uu in front of it), the way it faces (49152, -Y) and its size; draw the pad's live sign there |
| `pads[].centre`, `pads[].theme`, `railGap` | MatchPads | the court centre, the pad's default theme, the width of the rail openings |
| `themes`, `defaultTheme` | MatchPads | the theme ids with their titles and map code prefixes |
| `leaderboardAnchor`, `leaderboardYaw`, `leaderboardSize` | AimRange, Course | the centre of the leaderboard board's face, its facing (AimRange 0 = +X, Course 16384 = +Y), its size |
| `stalls` | AimRange | the five stalls' y ranges along the firing line |
| `route`, `checkpoints[]` (`id`, `kind`, `at`) | Course | the track's y range and the seven checkpoints (CP1 vault ... CP7 finish) |
| `screenAnchor`, `screenYaw`, `screenSize`, `musicAnchor`, `seats` | Lounge | the big screen's face, a point for music, the sofa centres |
| `signCentre` | Plaza | the centre of the hanging sign |
| `jukebox` | Plaza | the jukebox's foot [x, y] (the server's `HubLayout.Jukebox`; the map has a solid cabinet there) |

### Pad themes

Each pad has one render-only kit per theme: the court paint (lines, centre circle, the theme's title), a neon border,
skins over the shared rails and corner posts, props on the posts, and the theme's own coloured light baked into the
kit only. Collision is the same under every theme. The kits are gameplay mesh nodes named
`HubTheme_<pad>_<theme>_<material>`; the map shows each pad's default (`pads[].theme`) and hides the rest.
`HubPadThemes.Apply(padId, themeId)` shows another one on the current play scene's map (or remembers it until
`ApplyChosen(map)`), and `HubPadThemes.ThemeForMap(mapCode)` picks the theme for a map code by its first four letters.

| Theme id | Title | Maps | Look |
|---|---|---|---|
| `hub` | OPENSCCT | any other map | slate court, green lines, green neon, beacons |
| `polar` | POLAR BASE | Pola | ice court with blue lines, frosted rails, ice neon, antenna masts, cold blue light |
| `steel` | STEEL SQUAT | Stee, Fact, Ware | concrete court, yellow lines, corrugated rails, sodium lamp posts, warm sodium light |
| `aquarius` | AQUARIUS | Aqua, Offs | deep teal court, teal panel rails, pipes and valves, aquamarine light |
| `lab` | KRAUSER LAB | Krau, Clar, Deft, Ziop | white lab tiles, hazard-striped posts, green beacons, green-white light |
| `museum` | MUSEUM | Muse, Bank, Pana, Cine, Club, Sanc, Sche | burgundy court, gold lines, wood rails, marble plinths with gold, warm light |

Defaults: Pad1 polar, Pad2 steel, Pad3 aquarius, Pad4 lab. To add a theme: add an entry to `THEMES` in
`tools/hubmap/layout.py` (title, map prefixes, court colours, neon, rail and post tile kinds, light hue and
saturation, a props style in `kit_props`), add its tile kinds to `TILES` in `art.py` if new, add the id to
`HubPadThemes.ThemeIds` and its prefixes to `MapThemes`, regenerate. Each theme adds about 20 nodes and 15 KB.

## Scaling: interest management

A normal lobby supports up to 24 players and replicates every other pawn at its full schedule. With 50 players that
would be about 7 KB/s down per client (measured: 6.7 KB/s with the distance bands alone, all 49 others near the plaza)
and a server pass far over its frame. The hub opts in to three changes; a normal match is unchanged
(`NetDriverSettings.Interest` null, the shared send budget):

- **Per-connection budget.** The game's one shared send budget would split one client's rate between 50 clients; the
  hub gives each visitor an independent adaptive budget. Stock uses a 5 KB/s ceiling; Enhanced (the dedicated
  server default) uses 20 KB/s so 60 Hz replication does not starve nearby pawn snapshots and animation descriptors.
  `HubSettings.MaxClientRate` can override either ceiling. Snapshot age only counts pawns still present, so the last
  visitor leaving does not leave a permanent stale-snapshot warning.
- **Distance and rank bands** (`DistanceInterest`, rules 12 to 14 of the spec): the 4 nearest other pawns within
  2500 uu replicate as in a match; the rest of a crowd, and everything from 2500 uu, updates at most every 0.5 s, from
  4500 uu every 0.75 s, from 6500 uu every 1 s; beyond 7000 uu nothing new opens, and open channels close past
  8000 uu. The players' replication infos and the game state update at most every 0.5 s.
- **Proxy samples.** The decoded position sample schedule of other pawns is kept for the nearest 4; others are spaced
  10x, 14x and 18x by band.

## The soak test

The automated 50-client Hub soak was retired on 2026-11-07. With the expanded Hub, its 50 complete client
worlds reached 26.66 GiB working set in a local Release run and the hosted Linux CI shard shut down.

The long soak, two processes on one machine over UDP on localhost (Release):

```
svm-server --hub --port 28965 --loopback-only --seconds 330 --report 30 --report-file hub-server.log
svm-server --hub-bots 50 --connect 127.0.0.1:28965 --loopback-only --threads 8 --seconds 320 --report 60
```

Measured 2026-10-25 on the development PC (32 logical cores), 5 minutes with 50 bots in (25 spies, 25 mercs, side
changes and chat going on), the server's numbers per 30 s from its report:

| What | Value |
|---|---|
| Players | 50 of 50 in for the whole run, none dropped |
| Server tick time (60 Hz) | p50 0.56 to 0.79 ms, p99 14 to 26 ms, max 75 ms (whole run: p50 0.68, p99 17.6 ms); the p99 is the replication pass, every third tick |
| Server CPU | ticks busy 26 to 31% of the wall time; the process used 38 to 41% of one core (the loop's waits and the socket threads included) |
| Server memory | working set 116 to 142 MB, managed heap 58 to 71 MB; garbage collections only while the crowd joined |
| Download per client | 2.9 to 3.0 KB/s (server side, packet overhead included; the bots measured 3.0 KB/s mean, 3.0 KB/s max) |
| Upload per client | 5.4 KB/s (the move protocol of a bot that never stops turning) |
| Other pawns each client had | 49 (the hall is small enough that the crowd stays within the keep distance) |

For comparison, a four-player match uses 1.1 to 2.3 KB/s down per client (docs/NETCODE.md, "Bot matches"); this is the
worst case for the hub, every other player in view, and the plaza crowd at one spot. On the way:

| Change | Download per client | Server pass |
|---|---|---|
| Distance bands only, the game's shared send budget | the crowd starved (one client's rate shared by 50) | |
| Distance bands, per-connection budget | 6.7 KB/s | |
| Plus the nearest-pawn rank and spaced proxy samples | 3.4 KB/s | first 90% of the wall time (the nearest set was rebuilt on every question), then 21% once fixed |
| Final bands (4 nearest at the full rate, 0.5, 0.75 and 1 s) | 3.0 KB/s | |

The UDP run is all on one machine, so the link has no loss or delay; the bots ran in a second process on eight threads
(each with its own walk graph).

## Other checks

- `HubTests` (SimLink, virtual clock): players join on the side they ask for and spawn at the plaza at once; a side
  change gives a merc pawn on the server and on the client; chat reaches the others with the sender's name; every
  client has every other player's name tag; ten hits of 50 leave a pawn untouched; a leaver does not end the match and
  a late joiner plays and gets the message of the day. Voice: with a spy talking (a 440 Hz tone), a merc 1500 uu
  away hears him at the falloff gain (heard level 0.61 of the tone for a gain of 0.61), a spy 4000 uu away is sent
  nothing (the relay counts each copy out of range) until he walks up to 1000 uu; the merc's own volume for the spy
  halves the mix, and his mute stops the server sending.
- `InterestTests`: far actors are culled and close again past the keep distance; spacing by band; without interest
  the driver is unchanged. `ProximityVoiceTests`: falloff, occlusion, volume, mute, smoothing, the relay filter with
  its hysteresis, 50 players on a line; the relay with the filter (Open mode, near and far listeners) and without it
  (unchanged); the playback's falloff gain on the decoded level, the low-pass through a wall, silence without a
  position, flat again once released.
- `pwsh tools/ci/godot-smoke.ps1 -Tests hub` (headless): a hub server on loopback and a merc guest, then the main
  menu's Hub button, Join, the play scene on the hub map with the hub HUD, the guest's chat line, his name tag on
  screen, the guest's voice (a tone) heard by the player, a side change to merc and O back to the main menu (passed
  2026-10-26: `pawn=SPawnAttaque_1Mesh side=spy chat=3 tags=1 voice=heard rms=0.025 gain=0.12 relayed=57
  switched=SPawnDefense_1Mesh tagsShown=1`; the tone's level is 0.212, times the gain 0.12 at the distance between
  the two plaza rings). Pawns are drawn only with the converted characters, which the worktree has
  not; check that in the main checkout.

## Match pads (H2)

Four pads east of the plaza (`MatchPads` in `fixtures/hubmap/areas.json`, Pad1 to Pad4) each queue for one match,
in the spirit of a park's courts: walk onto a pad and you have "got next", the sign over the pad shows who is waiting,
and the match starts when enough players stand there. The rules are in [Hub](../spec/additions/hub.md) ("Match pads").

**Config.** `fixtures/hubmap/hub.json` (found beside the hub map; without it the same four pads are built in):

```json
{"countdownSeconds": 10, "matchPorts": [28890, 28897], "spectators": 4, "matchHost": "optional address players use for matches",
 "pads": [{"id": "Pad1", "map": "SteeE", "mode": "Story", "teamSize": 2, "bots": "off",
           "choices": [{"map": "PolaE", "mode": "Story"}, {"map": "AquaE", "mode": "Story"}]},
          {"id": "Pad3", "map": "MuseT", "mode": "Story", "teamSize": 3, "bots": "fill", "minPlayers": 2}]}
```

`map` is the map code; `mode` is Story, DiskHunt, Deathmatch or MercDeathmatch and is checked against the map
catalog's modes for that map (a mode the map does not play becomes `DefaultMode`; a map the server does not have
turns the pad off). `teamSize` is per side (Merc Deathmatch: players in the free-for-all). With `"bots": "fill"`
and `minPlayers` the countdown starts with that many ready people and bots take the empty places. The pads' places come
from the area manifest: each pad's `lo`/`hi`, and `signAnchor` [x, y, z] and `floorZ` when the map gives them (else
the sign floats 360 uu over the pad's centre). `HubPads.Reconfigure` changes a pad's match while the hub runs;
clients are told and their pad hook runs.

**Pad laptops** (they replaced votes). Each pad has a laptop on a desk outside its south rail, west of the gap
(`HubPadArea.Laptop`: 112 uu in from the pad's west edge, 112 uu south of it; a code-built prop, sent as a `laptop`
mark, whose desk is solid in the map's collision: `layout.py` `PadnDesk`, 192 x 128 x 72, with an LG on its south face). Anyone standing within 200 uu of it presses U (`hubplay use`; the hub answers
`hub laptop <pad>`) and gets the pad's match as a lobby's Session Parameters (`HubLaptopPanel`): map, mode, Max
Players, voice, auto balance, alternate rules, time, lives, respawn, disks, bots and practice. There is no password,
games in a row, revenge, random map or voice in lobby, because a hub match is one game with its own password and a lobby that launches at once.
Apply sends `hub padsetup <pad> <options>`. The hub checks the player still stands at that laptop, the pad is not
starting, it has the map and the map plays the mode. It then sets the pad's match: team size is half of Max Players
(rounded up, at most 12), or all of it (at most 24) in Merc Deathmatch. Bots fill needs hub.json's `minPlayers` (else 1) people,
and bots per side needs the places the bots leave. The queue stays; everyone on the pad is un-readied, the
countdown starts over and they are told who changed it. Every client gets `hub padsetup <pad> <changed> <options>`
when a pad's settings change, and the match starts with them. A changed pad that nobody queues on for 120 s goes back
to its hub.json match. The sign's last line shows the rules in short. An older game's `hub vote` gets a message
pointing at the laptops; hub.json `choices` are no longer read.

**Maps from OpenSCCT-Content.** Choose Map on a laptop lists the player's maps and every map in the
OpenSCCT-Content index (docs/CONTENT-PACKS.md). A map the player lacks shows its size and a Download button.
- **Player's copy:** a player queued on a pad whose map they lack gets it downloaded while they wait; the queue
  panel shows the progress. `HubService.WatchDownloads` refreshes the catalogs when a download lands.
- **Hub's copy:** when a laptop applies a map the hub lacks, the hub downloads it (`HubSettings.Content`) and
  switches the pad once it is in. `svm-server --hub` takes server packs (manifest and sim only) into `packs/` beside
  the server; a hub a game hosts takes whole maps into the player's own packs.

**Watching.** H on a pad whose match is in its lobby or being played asks `hub watch <pad>`; the hub answers with an
offer as a spectator (team 255, the match's password) and the player joins it as one (`?Team=255`), then comes back
beside the pad at its end as the players do. Each match has `spectators` places (default 4); watchers do not keep a
match open. The pad's sign shows the match in play: the score ("Playing: Spies 2 - Mercs 5"), the time left and how
many watch (`IHubMatch.Live`, written by the match's thread).

**Through the master.** With a master link, each pad match registers on the hub's master as an unlisted game server
(`HubMatchLauncher.Master`), and the offer carries its master id as a trailing field
(`hub match ... <host> <masterId>`). The game then asks the master the way in, as for a listed server (its address,
punch and relay routes, and a signed-in player's ticket for the match), and falls back to the hub's address when the
master does not answer. The hub holds a start's offers until the match's master entry is known, at most 4 s.

Defaults: Pad1 Enhanced Steel Squat Story 2v2, Pad2 Enhanced Polar Base Deathmatch 2v2, Pad3 Museum 3v3 Story 3v3
(bots fill, 2 people start it), Pad4 Enhanced Club House Merc Deathmatch 4 players (bots fill, 2 people start it).

**Flow.**

1. The server checks each tick which pad every living pawn stands on (its location inside the pad's rectangle, up to
   400 uu over the floor). Stepping on queues the player; stepping off or leaving the hub takes him out.
2. The lineup is the queue in order: his hub side when it has room, else the other side, else "next in line".
3. Each player in the lineup presses U on the pad to ready up (U again unreadies; the sign shows "Ready n/m"). With
   the lineup at the pad's need and everyone in it ready, a 10 s countdown runs (the sign and the HUD count down);
   anyone stepping off below the need, or unreadying, stops it.
4. At zero the hub opens the match session (`HubMatchLauncher`): a dedicated `OnlineServer` on its own thread of the
   hub's process, on the first free UDP port of 28890-28897 (clear of the hub's 28886 and the game's 28888), the pad's
   setup, bots per the pad, a fresh password as the join token, players ready on joining, launch once everyone came
   (or 20 s after the first did). Each lineup player gets `hub match <pad> <port> <team> <token> <map> <mode> <host>`.
   Players on the pad who were not in the lineup keep their places, and the pad goes on queueing (several matches of
   one pad can run at once).
5. The client leaves the hub (a hub this PC hosts keeps running) and joins the match through the normal online path
   (`OnlineService.JoinFromHub`: the lobby screen for a moment, then the round).
6. When the round is over (the results, after the 10 s end banner), or the player leaves or loses the match, he joins
   the hub again with `?HubReturn=<pad>` and is put just off that pad's south edge 1.5 s after he spawned. A note on
   the HUD says why ("The match is over. Back at the pads.", "Could not join the match: ...").
7. The session closes once its players left after the round (or 45 s after it ended), or when nobody came within
   45 s. A start that fails (no free port, map missing) tells the lineup why and retries after 10 s.

**Signs.** `HubSigns` is a generic world text: `hub.Signs.Set(id, position, text, rgba, scale)` and
`hub.Signs.Remove(id)` on the hub's thread (text up to 360 characters, `\n` breaks lines, colour 0xRRGGBBAA). A player is
sent a sign once his pawn is within 7000 uu of it (the interest layer's open distance), its changes while within
8000 uu, and its removal past that, four times a second. The client draws each as a `Label3D` billboard in Inter
with a dark outline, fading out from 60 m. The pads use ids `Pad.<padId>`; the minigames' leaderboards
(`PublishBoard(id, text)`) are meant to map onto `Set` with their own ids.

**Client.** `HubClient` keeps `Signs`, `Pads` (each pad's map code, mode, team size, state and lineup), `Queue` (the
player's own line) and `Offer`. `HubPadView` lays a translucent overlay with a border on each pad, coloured by state
(white idle, yellow waiting, green counting down and pulsing, cyan starting, grey off), and floats the map's own
picture from this install's catalog (the host page's map list picture, `MapCatalogEntry.PreviewImage`) over the sign.
`HubPadView.OnPadConfigChanged(padId, mapCode, mode)` runs when a pad's match is first known or changes: the hook for
the pads' map themes. The hub HUD shows the queue panel top centre while on a pad ("Queued on Pad 2 as a merc: 3/4,
waiting for 1 merc", "starting in 7 s", "this match is full; you are next in line (#1)").

**Trying it.** Two PCs: one hosts (main menu, Hub, "Host", or `svm-server --hub`), the other joins by
address; both walk east from the plaza onto Pad 1 (for a quicker check, set Pad1's `teamSize` to 1 in hub.json). On
one PC: two game instances (the second with `--svm-background` if it should stay out of the way), both joining
127.0.0.1. The hosting PC needs UDP 28886 and 28890-28897 open for players who reach it directly (not through the relay); set `matchHost` when
players reach the hub through an address the hub cannot see (a relay or a DNS name).

Checked 2026-10-26 with a headless diagnostic (a hub on loopback UDP, two visitors put on Pad1 at 1v1): both queued
(the second as a merc, the spies being full), the countdown ran, the match session opened on 28890, both joined it,
travelled to SteeE and played the round; when they left, the session closed and the hub logged it; a visitor
joining back with `?HubReturn=Pad1` was moved beside the pad. Not checked in a windowed game yet: the signs, the
overlays, the pictures and the HUD panel are built but unseen.

## Proximity voice

The hub's voice is the match's voice chat (docs/VOICE.md: Opus frames over the game's transport, the server relay,
the jitter buffers, push to talk and voice activation) with the positional module of `OpenScct.Net.Voice.Proximity`
on top. A normal match is unchanged: the relay's filter and each talker's spatial mix are null unless the hub sets
them.

- **Mode.** `SHubGameInfo.StartHub` sets the voice mode to Open, so spies and mercs hear each other (the team channel
  still reaches the talker's side only).
- **Server.** `HubServer` wraps its transport in a `VoiceTransport` and runs a `VoiceRelay` over the stock directory
  (`NetDriverVoiceDirectory` with the SBase links) with `VoiceRelay.Proximity` set: after the stock routing allows a
  listener, `ProximityRelayFilter.CanHear` must too (3000 uu, 3400 while the pair is in range), or the copy is not
  sent (`VoiceRelayStats.OutOfRange`). Positions come from `HubVoicePositions`: each living pawn's view point by
  talker id (PlayerID plus one), rebuilt once per world tick. A player who leaves is forgotten when the roster drops
  him. Mutes are the listeners' own (their Join packets), as in a match. `HubSettings.Voice` turns it all off;
  `HubSettings.ProximityVoice` holds the distances. The hub's status line adds the voice counters.
- **Client.** `HubClient` wraps its transport for voice; `HubVoice` keeps a `VoiceClient` connected, names the
  talkers from the replication infos, keeps the player's volume and mute for each by name, and every frame runs
  `ProximityPlayback`: for each talker, `ProximityListener.Update` from the listener's ear to the talker's pawn (the
  same `HubVoicePositions`, on the client's world), occluded when the map's BSP blocks the line
  (`PhysicsLineOfSight`, as relevancy uses), with the player's volume and mute for him. The mix goes to
  `VoiceTalker.Spatial`, which the playback applies to the decoded samples: the gain (it holds the player's volume,
  so it replaces it) and a one-pole low-pass at the mix's cut-off (`ProximityVoice.LowPassCoefficient`; none in the
  open). A talker with no pawn is silent.
- **Game.** `HubVoiceChat` (a child of `HubService`) owns the microphone and playback nodes (`VoiceAudio`, one
  generator per talker on the voice bus), reads the voice keys (not while the chat line is open), takes the Voice
  page's settings and passes the play scene's camera as the ear. The hub HUD shows the voice overlay top right (who
  is heard and at which volume, "Your microphone is muted").

## Activities (H3)

Things to do while waiting, server-authoritative (rules A1 to A12 of the spec). `HubServer` makes a
`HubActivities` (unless `HubSettings.Activities` is off) and ticks it after the host's tick; it reads where things
are from the map's area manifest (`HubLayout`) and keeps the boards in `HubScores` (`svm-server --hub` writes
`hub-scores.json` next to the server; with no `ScoresPath` the boards last for the session only).

| Activity | How it plays | Board |
|---|---|---|
| Spy assault courses | leave the start gate to start the clock; follow the route to the summit, passing its validation gates in order; falls return to a checkpoint while the timer continues | separate fastest-time boards |
| Merc aim range | U at the yellow firing line: a 30 s round, two pop-up targets at a time downrange (yours red, other rounds grey); 10 per body hit, 25 per headshot, plus up to 50 for accuracy | best round scores |
| Plaza ball | walk into the orange ball to kick it; into the west or east goal scores for the last kicker | most goals |
| Emotes | G then 1 to 8, or `/wave` in chat: your character does it (a pose laid over its animation) and the caption shows under your name, for everyone within 4500 uu; moving ends it | |
| Jukebox | the box in the plaza's north-east corner; U next to it changes the record for everyone | |

- **Hits without damage.** The hub server's level gets a hit-zones service (`HubRangeHitZones`) that wraps the stock
  trace: after the world and pill passes it tests the raised targets and returns the nearer hit. The rifle's own
  `TraceFire` then treats the target as any non-pawn actor and calls its `TakeDamage`, which only scores (when the
  level's `PlayRules.RangeTargetsTakeHits` is on, as in `PlayRules.Hub`). No pawn is touched, so no damage reaches
  anyone. Targets are not spawned or replicated: the clients draw them from `hubplay target` lines.
- **Boards.** Every board text goes through `HubActivities.PublishBoard(id, text)` (ids `CourseBoard`, `CourseBoard.medium`, `CourseBoard.hard`, `RangeBoard`,
  `BallBoard`), which sends a `hubplay board` line to everyone (and to each joiner) and raises
  `HubActivities.BoardPublished` for the world signs. The HUD's board panel shows the nearest board within 2200 uu
  only when the world has no sign for it (a board is read once). A board stands at its area's `leaderboardAnchor`
  when the manifest has one.
- **HUD.** `HubHud.Activities.cs`: the course clock and checkpoints or the round's clock and score, notices (hits,
  personal bests, records, goals), the board panel, a prompt of what U does here, the emote menu; Inter and
  JetBrains Mono, the menus' fonts (OFL, `game/branding/fonts`). Emote captions go under the name tags.
- **World.** `HubProps`: the gates (rings and posts), the firing line, the targets (they flip up and down), the
  ball (run on between the server's updates with the same friction), the goals and the jukebox, all plain shapes.
- **Sound.** `HubAudio`: a small synthesiser of our own makes the jukebox's four tracks (pad, bass, arpeggio and
  hi-hat over four chords: "Plaza Drift", "Night Shift", "Rooftop Run", "Static Lounge"), on the music bus and faded
  by the distance to the jukebox, and the chimes (start, checkpoint, finish, personal best or fanfare, hit,
  headshot, round over) on the flat effects bus. No recorded audio: nothing to license.
- **Checked** (2026-10-26, a scratch headless run over SimLink with a spy and a merc client, nothing committed):
  the spy's run started on leaving the gate, the finish refused him with checkpoints missing, then 3/3 checkpoints
  and a 6.00 s run, personal best, first place, the fanfare and the board on the other client; the merc's round with
  44 rifle shots through `ServerFireWeapon` aimed at the raised targets scored 44 hits and 15 headshots (715 points),
  neither pawn lost health; the merc's wave reached the spy 300 uu away; U at the jukebox changed the track on the
  other client; a kick moved the ball on both, a ball sent into the west goal scored; the boards came back from the
  scores file on a second start.

### Easy: the preserved layout-v3 route

The course south of the plaza is a raised route of decks over a pit (the course floor), along y -10368 from x 3072 to
9472 (`tools/hubmap/layout.py`, "Spy assault course"). Each deck reaches the next only through its piece, and the
decks are 384 wide with drops on both sides, so nothing can be run past or round:

1. climb onto deck A (a 208 lip, hang and climb) from the start apron;
2. a fence (376) across deck A's full width;
3. hand over hand across a 640 gap: the bar 277 over the decks, from 192 over deck A to 192 over deck B;
4. ledge shimmy across a 512 gap: a wall on the north side with its lip 264 over the decks, and a soffit 112 over
   the lip so the wall top cannot be climbed and walked;
5. a split jump chimney: deck C narrows to the 192 corridor between two walls, up to deck D's lip 408 over it;
6. a wall jump to deck E's lip 408 higher;
7. the zip line (TestRange's 1760 run, 320 drop) down to the finish deck, and a ladder down from it.

Each checkpoint stands on a landing deck at its height (radius 192, 48 deep: `HubGate.Depth`; areas.json's
checkpoints carry `radius` and `depth`), so it counts only once its piece is behind you. A running spy whose centre
goes under `fallZ` (96, the manifest's course area) once past the first checkpoint fell into the pit: the server
puts him back on the last passed checkpoint's deck using the shared collision-checked placement and movement reset,
the clock still running, with `hubplay run fall <falls> <checkpoint>`, a notice, a
chime and the falls on the HUD's clock. The additional perimeter grabs advance Easy to score revision 4; future route
changes must bump its own revision. The heights are the garden's kit
heights; the route is laid out by numbers and needs walking with the full moveset (the chimney's split jump up to a
408 lip and the shimmy under the soffit most of all).

### Emotes as poses

The character packages hold no emote clips, so each emote is a short program of bone turns (`HubEmotePoses`) laid
over the pawn's own animation: `SimPawnView.Overlay` (an `IPawnPoseOverlay`) gives turns about the pawn's frame
(forward, right, up), the view turns them into skeleton space and `ChannelPoseApplier.ExtraTurns` applies them parent
first after the blend and the aim; `ExtraShift` lowers the hips for sit. Each blends in and out over 0.25 s. The
server ends an emote for everyone near when its player moves faster than 60 uu/s or jumps (`hubplay emote <id> -`).
The angles are our own, tuned by eye, and want a look in the game.

## Accounts and moderation

- **Tickets.** Joining a listed hub, a signed-in player's game asks the master with his session, and the answer carries
  his ticket for the hub; the visit sends `?SvmTicket=` and `?SvmId=` (`HubClientSettings.Ticket`, `InstallId`).
  `HubServer` checks it with `TicketGate` (signed by the master's key, for this hub's master id, not expired, each
  ticket once): a signed-in player comes in under his account's name, a guest of a hub on a master as `Name#tag` (as on
  the online servers). `HubSettings.RequireAccounts` refuses guests. A hub hosted or typed in by address has no ticket
  to give; its visitors are guests.
- **Bans.** `HubSettings.Bans` (a `BanList`; `svm-server --hub` keeps `hub-bans.json`) by account, install id,
  address or name; the master's account bans kick (`MasterLink.KickAccounts`).
- **Admin portal.** The hub's master link reports its signed-in accounts and players (name, side, ping, account) to
  the portal's server page; a trusted hub (a `serverKey`) takes the portal's commands through `HubServer.Execute`:
  `help`, `status`, `players`, `say <text>`, `kick <id|name> [reason]`, `ban <id|name> [minutes] [reason]`,
  `unban <account|install id|address|name>`. Closing the hub is refused.
- **Looks (skins).** As in a match: the visit sends the Armoury's looks for both sides as `?SvmLookS=` and
  `?SvmLookM=` (`HubClientSettings.SpyLook`, `MercLook`); `HubServer.FilterLogin` checks them against the ticket's
  unlocks (`OnlineServer.CheckedLook`, `UnlocksOf`; a guest wears the retail look) and logs the player in with
  `?SvmLook=` for his side, so `SGameInfo.Login` puts it on his PlayerReplicationInfo, which replicates it to every
  client (`PlayerLookNet`); the pawn views redraw it (`SimPawnView.DrawCosmetics`, `PawnAttachments`,
  `FirstPersonView`). `HubPlayer` keeps both sides' looks: a side change puts on the new side's (`WearLook`). A save
  of other looks during the visit (`SettingsStore.Saved`, `HubService.OnSettingsSaved`) sends `hub look <spy> <merc>`,
  checked the same way (an older hub ignores the line). The host of a hub hosted in the game logs in with
  `?SvmHost=<key>` (`HubHost.HostKey`): his looks and rank go by his own account (`HubSettings.HostAccount`), as a
  listen host's; he is still not signed in (no ticket).

## Player list and chat

- **Player list (P).** Both sides' players (signed in ✓ or guest, ping) from `hub who` lines the hub sends every 3 s
  (`hub gone` when one leaves); for each other player a voice volume slider (0 to 200 %), a voice mute (`HubVoice`, kept
  by name for the visit; the relay stops sending a muted talker) and a chat mute (`HubClient.MuteChat`, this game only).
  The pointer is freed while it is open.
- **Local chat.** Tab while typing cycles everyone, your side, near you; `/l text` says a line near you. The client sends
  `hub local <text>`; the hub delivers `hub local <playerId> <team> <text>` to the players within 3000 uu of the
  speaker (`HubK.LocalChatRange`, the voice's hearing range), at most one line per half second. The chat box marks
  them "(near)".
- **Bubbles.** Any chat line a player says shows under his name tag for 6 s (two short lines at most).

## Limits and manual checks

- Test match pads in a windowed game: laptop settings, signs, queues, spectating, a full round and the return
  to the hub. Headless checks cannot establish the visual result or the full public connection path.
- Walk the assault course, rails and props with both movesets. Review activity HUDs, emotes, sounds and
  cosmetics with converted content. Generated geometry and offscreen renders do not replace this check.
- Check proximity voice with two microphones, including distance falloff and obstruction muffling.
- Pad matches run inside the hub process. Hosting each in a separate process would require a supervisor.
- An in-game host joins their hub over loopback without a master ticket. They appear as a guest, while their
  cosmetics and rank use their account. The Armoury is in the main menu, so opening it ends the visit.
- After changing side, check the pawn, equipment, camera, HUD and key layout together. Scene settings chosen
  on entry may need separate handling.
- At 50 players, the recorded benchmark's replication pass produced a spike every third tick. Re-measure
  after networking changes; the historical numbers above are not a current performance guarantee.
