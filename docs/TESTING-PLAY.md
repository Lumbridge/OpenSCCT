# Testing play the way a player plays

Smoke tests that poke `PlaySession.Input` or check only simulation state can pass while the game is broken for a
person at the keys. This page describes the real-play harness: `KeyPilot` presses the player's bound keys as Godot
input events, `PlayInspector` checks every frame what the player sees and hears, and the `play-inspect` smoke puts
the two together on SteeE.

| Path | What |
|---|---|
| `game/Scripts/Smoke/KeyPilot.cs` | `KeyPilot` and `KeyStep`: presses bound keys and moves the mouse through Godot input events |
| `game/Scripts/Smoke/KeyPilotProbe.cs` | a node that sees the events KeyPilot sends, to prove they reach the scene |
| `game/Scripts/Smoke/Inspector.cs` | `PlayInspector`, `InspectChecks`, `InspectIssue`: the per-frame checks and the report |
| `game/Scripts/Smoke/InspectPose.cs` | animation and feet checks of one drawn pawn |
| `game/Scripts/Smoke/InspectNet.cs` | counts the server's position corrections and reinits that reach the client |
| `game/Scripts/Smoke/PlayInspectSmoke.cs` | `--svm-smoke=play-inspect`, and `PlayInspectSmoke.StartPlay` for other smokes |

## The input path under test

```
Input.ParseInputEvent(InputEventKey / InputEventMouseButton / InputEventMouseMotion)
  -> viewport -> PlayInput._UnhandledInput -> PlayInput.FillTick (bindings: buttons, exec commands, axes)
  -> PlaySession.Input -> client SPlayerController (buttons, MouseLook, PlayerCommands)
  -> client tick, move protocol over loopback -> listen server on its own thread (PlayHostMode.Thread)
```

This is the same path as `--svm-play=SteeE`: wall clock, host thread, HUD on. Nothing is written to the session's
input or to the simulation by the test.

Two things can differ from a person at a window, both forced by running without one:

- With no window (`--headless`) the pointer cannot be captured, and `PlayInput` reads no game input while it is
  free. `KeyPilot` turns `PlayInput.RequireCapture` off there. With a window it clicks to capture as a player does
  and only turns it off when the capture does not take (the window has no focus). `Notes` says which.
- When the events sent with `Input.ParseInputEvent` do not reach the scene, `KeyPilot` also hands them to
  `Viewport.PushInput`, which runs the same `_Input`, GUI and `_UnhandledInput` passes. It finds out by sending
  F24 (bound to nothing) first and watching for it with the probe. On Godot 4.7.2 parsed events reach the scene
  both with and without a window, so the fallback is not used today.

A headless window is 64 x 64 pixels. The mouse look scales a turn by the viewport size (`MouseLook`), so timeline
looks are given for a 1280 x 720 window and scaled to the real one: a look turns the same with or without a window.

## KeyPilot

```csharp
KeyPilot keys = KeyPilot.Attach(root);        // root: a loaded PlayRoot; turns its PlayInput on
keys.Press("BT_Up");                          // hold the key bound to BT_Up (W in the stock layout)
keys.Tap("ChangeSpeed");                      // press, release 3 frames and 70 ms later (middle mouse: speed)
keys.Release("BT_Up");
keys.Look(40f, 0f);                           // mouse motion now, in pixels, right and down positive
keys.Play([                                   // or a timeline
    new KeyStep("walk", 4.0) { Hold = ["BT_Up"] },
    new KeyStep("look", 2.0) { LookPerSecond = new Vector2(300f, 0f) },
    new KeyStep("jump", 1.0) { Hold = ["BT_Up"], Tap = ["Jump"] },
]);
keys.Update();                                // once per frame: setup, taps, timeline, look (wall clock)
```

- An action is named the way the layout names it: an input byte (`BT_Up`, `bCrouch`, `bFire`), an exec command
  (`Jump`, `ChangeSpeed`, `SelectGadget 1`, `ShowMenu`), an alias (`Crouch`, `AliasHaut`) or a key itself
  (`IK_C`, `IK_Escape`). `KeyOf(action)` gives the IK key the player's bindings use for it (the user file
  `user://svm_keys_spy.ini` over `Default_kbd_0.ini`), so a rebind is followed. When several keys carry the
  action, the one with the fewest other actions is used. An action no key is bound to is listed in `Missing`.
- `Ready` is true once the delivery and capture are set up (a few frames); the timeline starts then. `Step` is the
  label of the running step (`""` before and after), `Done` is true when the timeline has run out. Keys are released
  between steps unless the next step holds them too, and at the end.
- `Sent` and `Delivered` count the events sent and those the probe saw; `Notes` lists the delivery, the viewport
  and the capture choices.

## PlayInspector

```csharp
var inspector = new PlayInspector(root, InspectChecks.All);
inspector.Attach();                            // once the session exists: hooks the client tick and the move calls
inspector.IssueAdded += issue => ...;          // first sighting of an issue (screenshots)
inspector.Phase = keys.Step;                   // issues are tagged with the step they were first seen in
inspector.Frame();                             // once per frame (wall clock)
inspector.ExpectPauseMenu();                   // right after pressing Escape
inspector.Finish();                            // end-of-run checks; Report() runs it too
IReadOnlyList<InspectIssue> issues = inspector.Issues;
string report = inspector.Report();            // one line per issue, then the counters (Summary())
```

Each check can be turned on alone (`InspectChecks` flags). An issue has an area, a subject (a pawn, a clip, a
stance), the message of its first sighting, the step and time it was first seen in, how many frames, windows or
calls it was seen in, and its worst sighting. Issues of the same area and subject are merged.

| Flag | Fails when |
|---|---|
| `Animation` | a drawn pawn moves (over 60 uu/s across a half-second window) while no bone of its drawn skeleton moves relative to the skeleton by 0.5 uu; channel 0 keeps the same sequence and frame while the pawn moves, or is empty; a channel names a sequence the simulation has no clip for or the drawn mesh has no animation for (each listed by name); a pawn moved and was never drawn animating |
| `Feet` | the lowest foot or toe bone (`B L Foot`, `B R Toe0`...) of a drawn pawn walking on a floor is more than 8 uu under the floor or 30 uu over it. The floor is a trace down from the pawn on the client's collision. Bones the animations do not pose (`ATT_02_HEAD`, `HOLSTER`) are left out |
| `Camera` | the camera point is inside world geometry, has no floor under it (outside the level), is under the floor the pawn stands on, or in third person is nearer the local pawn's axis than 22 uu (the original pulls the camera in to 30 uu next to walls, inside the cylinder: docs/ANIMATION.md, third-person camera next to walls). Frames where walls hide the pawn from the camera, which the original has too, are only counted (`hiddenPawnFrames` in the summary) |
| `Audio` | no `SoundSystem` is in the scene, it started no voice, with a real audio driver no bus peaked above -150 dB, or gameplay raised no sound event on the client |
| `Corrections` | the server sent more than 2 position corrections per 10 s (`MyShortClientAdjustPosition`, `MyClientAdjustPosition`; the report splits them by step and gives how many the client applied), reinit the pawn (`ClientReinitPawnBecauseOfDivergences`), or the local pawn moved more than 40 uu in one client tick beyond what its velocity explains |
| `Hud` | the play scene has no HUD, or the HUD never showed the spy or merc page |
| `PauseMenu` | 1.5 s after `ExpectPauseMenu` no visible node is in the group `svm_pause_menu` and no visible node's class is named `*PauseMenu*` or `*InGameMenu*`. A pause menu joins that group so the check does not rely on its name |
| `Input` | for the smoke's own checks of what the keys did (a walk that did not move the pawn with no wall in the way, a crouch, stand, speed change or jump that did not happen, an unbound action, events that did not reach the scene) |

Thresholds are the `Inspect*` constants in `PlayInspector`: test estimates, not game values.

`Summary()` gives the counters: seconds, client ticks, seconds moving, corrections received (short, full),
applied, reinits, snaps, sound events on the client, whether a sound system was found, voices started, the
loudest bus peak, the HUD pages shown, and per drawn pawn the windows it animated in, the largest bone motion and
the range of its foot offset.

The inspector only reads the scene and the client world. Two exceptions: it wraps the client driver's handlers of
the three correction calls (the original handler runs first), and with `Quiet` (on unless `SVM_PLAY_AUDIBLE=1`) it
keeps the master bus at -80 dB so a windowed run at night stays quiet; the other buses still show the sound in their peaks.
`SVM_INSPECT_BONES=1` prints every drawn bone's height over the floor once per pawn, stance and channel 0 sequence.

## The play-inspect smoke

`--svm-smoke=play-inspect`: SteeE as the spy, Play Level rules, three bots at normal, host thread, wall clock, HUD.
Once the spy is drawn in a walking state, `KeyPilot` plays about 45 s: stand up (the spy spawns crouched), walk,
look around and back, walk back, fast speed, run, run back, strafe left and right, slow speed, walk while turning,
crouch, crouched walk, crouched still, stand, jump, jump forward, look up and down, Escape. The walks go back and
forth over one line so they stay in space already walked. Every check is on. It passes only when no issue was
found; otherwise it fails with every issue on its own line. The log has `SVM_INSPECT new` at each first sighting,
then `SVM_INSPECT keys`, one `SVM_INSPECT step` line per step (distance moved, yaw, crouch, speed, airborne,
physics, a wall close ahead or behind), one `SVM_INSPECT issue` line per issue and `SVM_INSPECT summary`.

```
tools\godot\4.7.2-stable\Godot_v4.7.2-stable_mono_win64_console.exe --headless --path game -- --svm-smoke=play-inspect --svm-smoke-timeout=200
pwsh tools/ci/godot-smoke.ps1 -Tests boot -ExpectedFailures play-inspect -TimeoutSeconds 200
```

With a window (no `--headless`) and `SVM_PLAY_SHOTS=<absolute folder under local/>` it saves the window every two
seconds and at each new issue (at most one a second), named by the time and the step.

The smoke is expected to fail until the issues it reports are fixed; fixing an area should make its lines go away
without loosening the thresholds.

## The play-team smoke

`--svm-smoke=play-team` (`game/Scripts/Smoke/PlayTeamSmoke.cs`): team mates helping each other on SteeE, launched as
the launchers do (Play Level rules, server thread, wall clock, HUD), the player with one bot on his side and the
other side's bots held still. Two legs, `SVM_TEAM_LEGS=spy,merc`; `SVM_TEAM_SKIP` leaves out step groups (`menu`,
`revive`, `coop`, `bot`); `SVM_TEAM_TRACE=1` prints the bot and the player twice a second. The scenario setup (the
mate placed in front, hurt, marked, knocked down; the player placed at a boost spot found under SteeE's ledges) runs
on the server thread in `ServerHost.BeforeWorldTick` through the game's own functions; every player action is a key:
Use held opens the menu (the HUD's rows are checked: "Heal Mate", "Unspy Mate", "Exit", the selected row), S moves
the selection, releasing Use acts; Use at a downed mate revives him (both clips and their sounds on the client); T
offers or takes a boost (the HUD's coop icon area drawn: Coop1 "offer a ladder", Coop2 "climb on him"). Then the
bot is let go and must heal, unspy and revive the player and boost him. With a window and `SVM_PLAY_SHOTS` it saves
the window every second and at each step's end. Each bot step prints `SVM_TEAM route` (whether the mate could be
placed near the player, and the route the bot's graph has to him), and a step that times out lists the clips and
sounds of both pawns and the mate's trail on the server (each state and team goal with where it began).

## Using it from another smoke

A smoke for one area can reuse the classes with only its checks on:

```csharp
public sealed class CrouchFeetSmoke : SmokeTest
{
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private PlayInspector? _inspect;
    private int _teardown = -1;
    private SmokeResult _result;

    public override string Name => "crouch-feet";

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown >= 0) return ++_teardown < 6 ? SmokeResult.Running : _result;
        _root ??= PlayInspectSmoke.StartPlay(context, bots: 0);   // the launcher's play setup, in the tree
        if (!_root.IsLoaded || _root.Session?.LocalPawn is null) return SmokeResult.Running;
        if (_keys is null)
        {
            _inspect = new PlayInspector(_root, InspectChecks.Feet | InspectChecks.Animation);
            _inspect.Attach();
            _keys = KeyPilot.Attach(_root);
            _keys.Play([new KeyStep("crouch", 1.0) { Tap = ["bCrouch"] }, new KeyStep("crouch walk", 3.0) { Hold = ["BT_Up"] }]);
        }

        _keys.Update();
        _inspect!.Phase = _keys.Step;
        _inspect.Frame();
        if (!_keys.Done) return SmokeResult.Running;
        _result = _inspect.Issues.Count == 0 ? SmokeResult.Pass(_inspect.Summary()) : SmokeResult.Fail(_inspect.Report());
        _teardown = 0;
        _root.QueueFree();                                   // stops the server thread
        return SmokeResult.Running;
    }
}
```

Wait for the pawn to be drawn in a walking state before the first key (see `PlayInspectSmoke`): the spy spawns
crouched in `s_PlayerWalkingCr0H`, and keys pressed before the controller has its pawn are lost.

## The play-reticle smoke

`--svm-smoke=play-reticle` (`game/Scripts/Smoke/PlayReticleSmoke.cs`): the merc's reticle against his shots and his
frag on TestRange, as the launchers play it. The server puts the merc in the shooting lane 200, 600, 800 or 1900 uu from
the berm; the keys turn him with the mouse, crouch, strafe and fire single shots, then select the frag (1) and throw it
(right mouse). For each shot the drawn centre dot (the merc page's draw list, `rond02`) and the drawn camera of the
frame the player clicked on are kept, and the impact mark the client draws must lie within the shot's spread of the
dot (plus the original's own one-unit dot offset). The camera's height over the floor is checked against the
original's (144 standing, 89 crouched). The frag must follow the original's throw (spawn and falling physics measured
in the sandbox, spec/equipment/grenade-throw.md rule 4a) within 4 uu; where it first bounces against the dot is
reported. With a window and `SVM_PLAY_SHOTS` it saves the window before and after every shot.

## Where the lasers leave the guns (play-lasers smoke)

`--svm-smoke=play-lasers` (`game/Scripts/Smoke/PlayLasersSmoke.cs`) plays SteeE twice from the keys. As the spy (one
merc bot): a tap of E draws the pistol with its laser, and crouched, standing, walking and aiming up and down the drawn
beam must start on the drawn pistol (the hand's 30 X - 4 Y + 8 Z, 3.5 uu behind the p190's FX bone), run along the
hand's X axis and end on the drawn dot; then the server turns the bot's laser on and holds it in view, and its beam
on the spy's screen must start at the FX bone of the rifle drawn in its hands. As the merc (one spy bot): Q turns the
laser on and his beam must start at his first-person rifle's FX bone pulled back 50 uu, drawn from the muzzle; then
he backs into the wall where the sandbox merc did (35 uu behind his eye) and a rifle shot and a frag throw must both
go off, as they do in the original (spec/equipment/grenade-throw.md, Resolved). Sandbox captures for comparison:
`local/lasers/capture.py --side spy|merc|wall`.

## Leaving a session and quitting (play-exit smoke)

`--svm-smoke=play-exit` plays two sessions in one process (`SVM_EXIT_SESSIONS` for more), each left the way the
in-game menu leaves one (the session stops, the main menu opens, the play scene is freed), then quits. A crash at exit
comes after the verdict line, so the run counts only with exit code 0 (`tools/ci/godot-smoke.ps1` checks it). Run it
with a window: headless has no rendering device, and the crash it guards against was a renderer one. The windowed
game used to end with a segmentation fault (exit 139) after a second session, after one `free_rid can only be called
from the render thread` error per frame of the left session: the post passes' `RenderSceneBuffers` wrappers were left
to the garbage collector, which then released a freed viewport's buffers on its finalizer thread. The render callbacks
now dispose the wrapper on the render thread, and `FaithfulView` frees its post passes' GPU resources when it goes
(no RID leak warnings at exit either).

```
tools\godot\4.7.2-stable\Godot_v4.7.2-stable_mono_win64_console.exe --path game -- --svm-smoke=play-exit
```

## Frame time (play-hitch smoke)

`--svm-smoke=play-hitch` (`game/Scripts/Smoke/PlayHitchSmoke.cs`) plays SteeE as the spy with three bots, launched as
`--svm-play=SteeE --side=spy --rules=playlevel --bots=3 --difficulty=normal` (host thread, wall clock, HUD, sound),
for 90 s of real time after a 3 s warm-up, and fails when any frame takes longer than 33.4 ms: two frames of a 60 Hz
display, so a visible hitch (a skipped picture) fails it while a single late present does not. Headless uses the same
limit: there the frame loop sleeps on the OS timer (15.6 ms steps on Windows), so frames of 16 to 22 ms with no work in
them are normal. Every frame over 20 ms (`SVM_HITCH_LOG_MS` to change it) is printed as `SVM_HITCH slow` with what ran
in it: the simulation and view milliseconds, the client ticks, the .NET collections per generation and their pause,
the bytes allocated (all threads and the main thread), Godot's pipeline compilations, the longest gap between server
ticks and Godot's process time. `SVM_HITCH summary` gives the frame time distribution (p50 to p99.9, max, counts over
20 ms and over the limit), collections and pause per minute, the allocation rate, the skeletal model files read and
reused, and the particle surfaces written in place. `SVM_HITCH_SECONDS` and `SVM_HITCH_MS` change the play time and
the limit. Run it with a window; `SVM_AUDIO_MUTE=1` keeps it quiet.

```
set SVM_AUDIO_MUTE=1
tools\godot\4.7.2-stable\Godot_v4.7.2-stable_mono_win64_console.exe --path game -- --svm-smoke=play-hitch --svm-smoke-timeout=300
```

What made the hitches a player saw every 15 to 30 seconds (October 2026, release export, windowed, 120 s):

| Cause | Fix |
|---|---|
| A respawned pawn read its character glTF again (scene, skin, clips, materials): 35 MB and 560 ms in one frame, at every respawn | `SkeletalModel.Load` keeps the generated scene per file and duplicates it; the clips' bone tracks are shared (`AnimSampler.For`) |
| Map emitters rebuilt their mesh from Godot arrays every frame: a Godot array, five packed arrays and their variants per emitter per frame, each a finalizable wrapper with a tracked handle. Every collection was a gen1 one of 10 to 35 ms (28,000 finalizable objects promoted each time, 180,000 handles) | `QuadBuffer` writes the quads into byte buffers and updates the surface in place (`MeshSurfaceUpdateVertexRegion`, `MeshSurfaceUpdateAttributeRegion`) |
| The Use menu's mate entries (HUD every frame, bots every tick) made a whole pawn or gadget to read a class default | the cached default objects (`SBasePawn.DefaultObject`, `SGadget.ClassDefaults`); no closure per call |
| Per-frame `StringName`, variant and wrapper churn: voice buses, global shader parameters, the post passes' and spot depth pass' uniforms, the render callbacks' `RenderData`, bus names | names made once, values set only when they change, uniform lists kept (`UniformSlots`), wrappers disposed at once |
| The first shot of a match loaded the muzzle meshes, effect textures and particle shaders and made the flash, tracer and decal materials' shaders (40 ms) | `FxLibrary.Warm` and `CombatFxView.WarmMaterials` at load |

Before: max frame 567 ms, 6 frames over 20 ms, 124 gen0 and 124 gen1 collections a minute with 1179 ms of pause a
minute, 37 MB/s allocated. After: max 15 ms, none over 20 ms, 55 gen0 and 5 gen1 a minute with 43 ms of pause a minute
(under 1 ms each), 16 MB/s allocated, nearly all of it the HUD's draw lists (Magma draw items and strings), which die
young and cost no visible time.

## Test windows stay in the background
Windowed test runs must not take the focus or the mouse of the person at the machine. Smoke runs, SVM_PLAY_SHOTS runs
and runs started with `--svm-background` (or SVM_BACKGROUND=1) move their window off-screen, drop its taskbar button and
never take focus (`BackgroundWindow`); the game still renders and saves its frames there. `Controls.Pointer` keeps the
real pointer free. Godot shows the window before any game code runs, so `game/override.cfg` (local, gitignored, excluded
from exports) starts it minimised and unfocused; `tools/ci/godot-smoke.ps1 -Export` writes the same file next to its
scratch exe. Copy it next to any other scratch export before a windowed run, and never put it in `local/testbuild`.
`SVM_SMOKE_FOCUS=1` turns all of this off for debugging. A separate Windows desktop is not an option: Godot's Vulkan and
D3D12 renderers fail there.

## Trusting the smokes (repeat runs, CI, flakes)

A smoke that fails one run in five teaches people to ignore it. `tools/ci/godot-smoke.ps1` can run the suite many
times and says which tests are flaky:

```
pwsh tools/ci/godot-smoke.ps1 -All -Repeat 5 -ExpectedFailures selftest-fail,play-inspect,range-traversal -Exclude map-shots
pwsh tools/ci/godot-smoke.ps1 -All -Repeat 2 -Windowed -ShotsDir local/shots ...           # with a window, muted
pwsh tools/ci/godot-smoke.ps1 -Exe local/x/OpenSCCT.console.exe -Tests play-team -Repeat 10   # an exported build
```

- `-All` runs every registered smoke (the list the game prints for an unknown name), less `-Exclude`, the expected
  failures and, headless, the window-only ones (`golden`). `map-shots` (every Story and Deathmatch map) always gets at
  least 1800 s. `-Repeat N` runs the whole set N times and ends with one `stable`, `FLAKY` or `failing` line per test.
- `-Windowed` drops `--headless` and sets `SVM_AUDIO_MUTE=1`; `-ShotsDir` gives each run its own `SVM_PLAY_SHOTS`
  folder. `-Exe` runs an exported console exe instead of building and running the editor; `-SkipBuild` reuses the
  build; `-LogDir` keeps one run's logs apart from another's.
- Other work rebuilds the checkout's game assembly while a long run goes on (it did during these measurements), so a
  before and after comparison is best made on two exports of the two trees (`--export-release` from a scratch
  worktree into a folder under local/, with a `shared` junction to `.content/shared` and `maps/testbox`, as a release
  has them).
- Smoke runs play the stock key layout: `InputBindings` neither reads nor writes `user://svm_keys_*.ini` while
  `SettingsStore.Isolated` is on, so a player's rebinds cannot change a smoke and a smoke never writes his file.
- Smokes find the checkout's tracked files (`fixtures/testrange/stations.json`, `tests/scenarios`) and `local/` through
  `SmokePaths`: the folder above the exe or the game assembly that holds OpenSCCT.slnx, as `MapCatalog` finds maps. In
  an export `res://` is the exe's folder, so `res://..` named `local\fixtures\...` from local\testbuild, and
  play-reticle, range-motion and play-traversal failed there. Outside a checkout the same relative path next to the
  exe is used (`-Export` copies the station manifest there). `MapBuilder.FindMapFolder` finds an export's checkout the
  same way (steee-render and audio failed in exports too).
- `play-hitch` measures frame times: run it alone. With other games running beside it (two suites at once) it saw one
  frame of 43 to 52 ms in some runs.

### CI

The `godot-smoke` job (windows-latest) runs the smokes that need no game content: `boot`, `testbox` (a tracked
fixture) and `range-sim` on a TestRange generated in the job with `tools/testmap/generate.py --no-classes` (no
install, so the generated class defaults); the station manifest it writes must equal the tracked one. `range-sim`
(`RangeSimSmoke.cs`) is launched as `--svm-play=TestRange --side=spy --rules=playlevel --bots=3` runs it: the player
walks forward and back with the layout's keys (KeyPilot; the built-in layout when the game's files are missing) and
must move, the three bots must reach the client and two of them must move 300 uu. Nothing drawn is checked. Every other
smoke needs converted maps and the install's characters, HUD and sounds, which never reach the repository (the
TestRange smokes too: without the HUD package and the character meshes they cannot check what they check). The logs
and the JUnit file are uploaded as the `godot-smoke` artifact.

### Godot --import crash

`--import` of a fresh project crashed now and then (exit 139, a segmentation fault in Git Bash; 5 of 72 runs here),
in the glsl importer (the log stops at `reimport | svm_display_post.glsl`) or at exit. A project holding only our three
`.glsl` files crashed the same way (1 in 30), an empty project never (0 in 30), and ours without its autoloads still
did (1 in 30): Godot 4.7.2's headless glsl import, which our compute shaders trigger. They are now imported with the
`keep` importer (exported as they are) and compiled on the rendering device at run time
(`Render/ComputeShaderSource.cs`, used by the display post pass, the vision blur and the spot shadow depth pass):
0 crashes in 40 imports of the full project and in 30 of the three-file project. The post pass draws as before (the
motion vision's clean rectangle in play-motion's windowed shots).

### Flakes found and what they were (October 2026)

Headless runs of an export of 62898d2 and of one of the fixed tree (five each), the editor runs, two windowed runs of
each export, and targeted repeats:

| Smoke | Before | Cause | Fix |
|---|---|---|---|
| play-reticle, range-motion, play-traversal, steee-render, audio | 0/5 in an export | `res://..` is not the checkout in an export | `SmokePaths`, `MapBuilder.FindMapFolder` |
| play-merc | about 1 in 5 (keys leg) | thrown 46 degrees off the merc's wall, the frag hit another wall 250 uu out, came back at a fifth of its speed (wall rebound 0.2) and went off 384 uu from him in 10 of 10 runs: inside its 500 uu range, which hurts the thrower in the original too (frag-grenade.md rule 3: the native skips no one); within 350 uu that is the flat 200 of an explosion and he died. Faithful | the throw faces the most open of 32 directions (two world lines from the eye, level and rising, 1500 uu), and the step checks the frag goes off over 520 uu from him: 835 uu in every run since |
| play-motion | 1 of 3 editor runs | the merc was placed where his only clear line to the held spy grazed a wall corner; the spy's run drifted a few units aside and the tracker (which needs sight) never locked. Which spot it was depended on where the bot had wandered before it was held | spots are ranked by how many of 15 points of the spy's run (250 uu each way, 40 uu aside) the merc sees: 25 of 25 runs since (editor, export, windowed), seeing 14 or 15 |
| play-team | 2 of 4 (Ryan's runs), 1 of 3 editor, 0 of 2 windowed export; with the smoke fixes but not the bot fix 3 of 5, and 5 of 7 spy legs | the bot mate backed off (the Use menu wants its body facing him) over the edge of SteeE's pit under pipe GE218 and fell 640 uu; or, after a drop into that pit on its own route, it reached the pipe and held it without climbing until the goal timed out (the menu goals gave no keys while the bot was not walking) | `TeamAi`: no back-off without ground behind (out to the staging point on the graph instead), and a pipe of the route is climbed (`TheBotClimbsAPipeOfItsRouteToHealThePlayerAbove` failed before): 5 of 5 editor, 11 of 12 headless export, 1 of 2 windowed since (the two open cases below) |
| play-audio-krau | 4/5 in the export of 62898d2 | the process ended with an access violation 2.5 s into the load, no backtrace | not seen again in later runs; open |
| map-shots | timed out (300 s) | a sweep of every map, not a flake | 1800 s minimum in the script |
| frame-rate, net-lobby-guest | failing headless | a window-only smoke, and the helper net-lobby starts | left out of a headless `-All` |

Still open: play-team's revive step once saw the server put the player in `s_ReanimateFriend_0H` while the client
never left `s_PlayerWalkingSt0H` (the revive and its clip played; the mirrored state never showed on the client), and
once, with a window, the merc's Use press made an attack (`AtakStAlCh2` and `AtakStAlTu` sounds) instead of opening the
mate menu (that run was before the windows were kept in the background, see above). The step's failure report keeps
the states, clips, sounds and trail for the next sighting. And once the in-progress goggle glow and laser work reached
the checkout's build (25 October, 19:59), steee-render printed its OK line and then died at exit in 4 of 4 editor runs
(an access violation in a `GodotObject` finalizer, `godotsharp_internal_object_get_associated_gchandle`); an export of
this tree without that work passed 5 of 5.
