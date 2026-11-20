# Local Chrome / WebGPU prototype

The production master has retired the browser game. The loopback lab explicitly enables its routes for development.

The existing C# client and server simulation run in WebAssembly through 2dog
4.7.2.186 (native payload 4.7.2.175). The default scene is TestRange. The native
Options menu, per-role bindings, saved loadouts, HUD and sound run in the browser.
The content bundle includes TestRange and the official server's SteeE.
Leaving a session opens a browser-specific native menu with both roles and lessons.

Prerequisites: .NET 10 SDK, `dotnet workload install wasm-tools`, Python 3, and a
desktop browser with WebGPU and hardware acceleration. Package downloads come
from the pinned official 2dog release and are SHA-256 checked.

From this worktree:

```powershell
./tools/browser/Build-Prototype.ps1 -ContentRoot C:/path/to/.content
./tools/browser/Start-Lab.ps1
```

Open <http://127.0.0.1:8878/fixture> for disposable synthetic account sign-ins,
or <http://127.0.0.1:8878/play/> for guest practice. Multiplayer connects to a local
TestRange server with an idle native UDP player. The lab binds to loopback and
keeps its database in memory. It does not use production accounts or credentials.
The two fixtures let you check account switching. Restarting the lab resets them.

After source edits, `./tools/browser/Publish-Prototype.ps1` publishes into a new
`local/browser-build/<build-id>` folder. `current.txt` identifies the latest
successful bundle. Restart the lab to serve it. A running session keeps its own
bundle; the build never deletes a folder being served. For a guest-only static
host, use `serve.py --directory <bundle>` (port 8877).

`Publish-Prototype.ps1` compiles managed game code to WebAssembly by default
instead of relying on the IL interpreter. It retains reflection-discovered game
classes through explicit trimmer roots, with IL retained for runtime fallback.
`-Interpreter` builds the slower comparison runtime. The default `-FastLink` uses
the smaller final whole-module `-O1` pass; the individual AOT assemblies still use
LLVM `-O2`. `-FastLink:$false` enables the much more expensive full `-O2` link for
experiments. `-AheadOfTime` remains accepted by earlier commands. The lab accepts an optional
`SVM_BROWSER_LAB_LISTEN` loopback HTTP address and `SVM_BROWSER_LAB_ORIGIN` for the
browser-facing origin. Both synthetic accounts start with normal vertical look.

The input button requests Godot's normal pointer capture from a user gesture.
Escape opens the native menu and releases the pointer. Embedded browsers which
reject capture can still use keyboard input, mouse buttons and mouse movement
within the canvas. Continuous captured mouse look has been verified in the user's
connected Chrome, with `captured: true` diagnostics and Escape release. Chrome must
have OS window focus: background clicks can fail with `WrongDocumentError`. Capture
is requested directly from the gesture, with raw movement where supported and
Godot's duplicate request coalesced. Fullscreen keeps the toolbar and coach available.

## Accounts and hosting

The loopback lab serves its bundle at `/play/` and provides synthetic website sessions.
It negotiates precompressed gzip siblings with the correct WASM MIME type.
`/play/profile` uses the lab session, CSRF protection, account identity and an
atomic profile revision check.

Settings use the existing desktop `ProfileSync` format, including both role
binding files. Browser-local files and unsynced edits are separated by account.
Offline edits stay local and retry; conflicting revisions pause before overwriting
another device's changes. The player can explicitly choose the account version.

Multiplayer uses the existing online client, lobby, travel and prediction through
a bounded binary WebSocket transport and a same-origin gateway to registered,
trusted game servers. Each grant expires after one minute and can be used once;
account tickets are server-scoped. Origin, CSRF, private-master invite keys,
account bans, queue limits and connection limits are checked. Server passwords
remain normal game login options. No client-selected UDP address is accepted.

The browser content mirror lives outside versioned game bundles. Use
`python tools/browser/sync_content.py --output local/browser-content --maps Entry AquaE` in the lab
to fetch the official index, verify published archive sizes and SHA-256 hashes,
and publish a same-origin map index only after every archive is ready. `--maps
Entry AquaE` selects a small local fixture. The lab serves only `index.json` and
hash-addressed ZIPs at `/play/content/`, with range requests and immutable archive
caching. `SVM_BROWSER_CONTENT_ROOT` overrides the directory. Shared runtime data
stays in the initial browser bundle; the desktop character archive is over 1 GB.

The existing ContentStore performs the browser install and manifest checks. Maps
install into the current session's content tree. Browser cache may avoid another
network transfer, but installed map files currently do not survive a reload.
The native Visit a Map route uses an inline host in the browser, while desktop
continues to use its own listen-server thread. Browser map installs also fetch
matching published bot navigation. Empty browser practice visits defer the bot
world until a bot is requested, retaining unlimited gear without a long startup
navigation build. Tutorial progress is kept in the account's browser-local files.

## Lessons

In the local lab, open `/play/?lesson=vision-cancelling` or `/play/?lesson=spy-grab`.

Vision practice starts on the TestRange tower. The normal physics, goggle input
and landing states drive feedback. The Walk off button holds forward through the
landing; the player can use their own movement/vision bindings instead. Recovery
time is measured from actual simulation ticks, with no artificial success window.
The coach retains recent attempts, switch timing relative to touchdown, baseline,
best recovery and measured savings. Walking through the green portal returns both
client and authority to the ledge without recreating the session or losing results.

Grabbing practice uses a stopped mercenary or one walking between two points.
Its AI is held and receives walking input only; practice tank protection applies.
Grab only feeds the normal client input and paired network action. The `grabarea`
overlay was brought over from the existing grab-area worktree; the coach's window
button and the native console command toggle the same drawing. The sector shows
the angle guide, while colour also checks the actual stance and collision probes.

The existing desktop **Tutorials** page also includes **Vision Cancelling** and
**Mercenary Grabbing**. These launch the same `BrowserLesson` simulation in a
normal TestRange play scene. A native coach retains landing results, adds the
return portal, and exposes reset, stopped/walking target and grab-area controls
beside the Escape menu. Leaving a desktop drill returns to Tutorials.

## Validation and limits

The browser has rendered TestRange on WebGPU Mobile, accepted keyboard movement,
mouse look and firing, drawn night/thermal vision, started native audio voices,
restored signed-in settings and saved/reloaded a changed binding. Separate accounts
did not inherit each other's bindings. The real paired grab, the `grabarea on/off`
console command, fullscreen and both wiki iframes have been exercised in Chromium.
One vision lesson measured 1,317 ms of normal recovery versus 583 ms after switching
vision during landing; these are individual attempts, not fixed timing targets.
Evidence and console logs are under `local`.

Focused integration coverage includes profile/session/CSRF/revision checks,
appearance revisions, compressed WASM serving, wiki embeds, non-attacking bot
movement, real grab initiation and shorter recovery after a vision switch while
continuing to move. These checks do not establish full desktop parity.

The native dashboard, Account, Stats, Achievements, Armoury and match history pages
are shared with desktop. In a browser their account calls use the existing HttpOnly
player session plus CSRF and expected-account headers; no desktop bearer token is
issued or stored in JavaScript. Settings use compare-and-save revisions, while
appearance uses the desktop appearance revision API. Browser sign-out ends the
website session and reloads into the guest's separate local settings.

An actual Chrome visit signed in through Discord, entered the official EU server
as the verified Ryan account, and loaded a SteeE round in 26.15 seconds. The user
confirmed 60 FPS with Chrome in the foreground and continuous captured mouse look
with Escape to release it. Occluded Chrome windows were heavily throttled during
automation, so their low frame rates are not representative of foreground play.
That SteeE result does not establish performance on every map: full Aquarius
offline visits are much slower and are being profiled separately. Local probes
include simulation/view costs, engine processing time, draw counts and managed
memory. Browser model prefetch and texture preparation must not synchronously
wait for worker tasks; the single-threaded build prepares those on its main thread.

A controlled Aquarius comparison used one game tab, the same spy spawn and an
1808 x 999 viewport: 20 samples had median 13 FPS with interpreted managed code
and 52 FPS with AOT (ranges 12–15 and 42–60). Simulation time fell from median
61.0 ms to 8.1 ms. This does not measure first-use movement or vision stalls.
Keep one game tab open during comparisons and stop native rendering tests and
compilers before recording performance.

Browser loading now draws batches of real scene materials and mesh layouts across
normal, night, thermal, motion and EMF modes, including hidden devices and both
first-person weapon rigs. Map-owned animated props are prepared before the client
binds them. `SVM_BROWSER_SCENE_WARMUP=1` enables the browser scene pass for native
renderer diagnostics. The repeat-warmup test passes on TestRange and SteeE with
zero new draw pipelines, including a real first rifle shot. Wet, shocked and cloaked
character materials and all shot/decal/tracer primitive layouts draw during loading.
The separate native first-use probe still finds seven additional
Forward+ thermal pipelines on SteeE. Chrome Mobile needs its own live check.
Browser probes include the frame maximum, vision mode and pipeline counters.
On loopback, `?profile` records every probe (five per second) for first-use checks;
ordinary local visits log once every two seconds. Hosted visits never upload logs.
Published bundles include `build.json` identifying their source and AOT settings.
Browser readiness also waits for the client map and player camera to bind, then
draws each vision twice on the actual gameplay view before restoring the controller's
mode. Initial simulation catch-up and the first full-view vision draws stay behind
the loading cover; no gameplay goggle state changes during this pass.
Offline browser visits recover from a delayed frame with at most two simulation
ticks instead of five, dropping stale backlog so window activation does not lead
to a series of expensive catch-up frames. Remote matches keep their prediction cadence.

Known limits:

- Forward+ has an upstream WebGPU texture-binding validation failure and draws a
  white world. Mobile is the working default. `?forward-plus` is diagnostic only.
- WebSocket uses TCP: packet loss can stall later frames, unlike native UDP.
  Microphone voice chat is currently disabled in browser visits.
- Scoreboards keep the native Brotli wire format. Browser clients decode native
  server transfers and browser listen servers encode them through managed
  BrotliSharpLib streams, initialized during loading. Transfer/output limits remain.
- WebGPU instance uniforms did not reach actor shaders reliably in the pinned
  engine. Browser actor-local material uniforms carry lighting, heat, EMF, tint
  and foreground depth. Desktop keeps its normal instance path.
- Browser world materials use their general lighting/vision shader, avoiding the
  desktop's extra shortcut variants and bundled shader prewarm lists.
- The content subset contains TestRange, SteeE, Entry, Lobby, shared gameplay/UI/audio and default
  character assets. Cosmetic variants are excluded; other maps download separately. The native
  EAX extension is not included. Browser audio uses built-in room reverb and
  source shelf filters, retaining separate direct/room sends, distance gains,
  authored zone strength and tail lifetime. Its room response approximates EAX.
  TestRange has no map reverb table.
- Official-server play needs its exact map version. The preview uses published
  SteeE 1.0.4, content hash `82760242e4ae1618`, from OpenSCCT-Content; the checkout's
  newer conversion differs. Pass its extracted folder with `-SteeEFolder` to
  Build-Prototype or `--map-source SteeE=FOLDER` to prepare_content.py.

Original converted content, engine packages, browser state, generated bundles,
logs and screenshots stay untracked. Console capture is restricted to loopback
hosts; hosted builds do not send console logs to the server.
