# Chrome / WebGPU prototype

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

The input button requests Godot's normal pointer capture from a user gesture.
Escape opens the native menu and releases the pointer. Embedded browsers which
reject capture can still use keyboard input, mouse buttons and mouse movement
within the canvas. Continuous captured mouse look has been verified in the user's
connected Chrome, with `captured: true` diagnostics and Escape release. Chrome must
have OS window focus: background clicks can fail with `WrongDocumentError`. Capture
is requested directly from the gesture, with raw movement where supported and
Godot's duplicate request coalesced. Fullscreen keeps the toolbar and coach available.

## Accounts and hosting

The master serves a bundle at `/play/` when `SVM_BROWSER_GAME_ROOT` points to its
directory (or an adjacent `browser` directory exists). It negotiates precompressed
gzip siblings with the correct WASM MIME type and same-origin embedding policy.
Production sign-in uses the website's existing Discord flow and HttpOnly session
cookie. `/play/profile` uses that session, CSRF protection, account identity and an
atomic profile revision check. No account bearer token enters browser storage.

Settings use the existing desktop `ProfileSync` format, including both role
binding files. Browser-local files and unsynced edits are separated by account.
Offline edits stay local and retry; conflicting revisions pause before overwriting
another device's changes. The player can explicitly choose the account version.
The live Discord authorization flow has completed successfully on the website.
The hosted game/profile check is recorded separately from local synthetic tests.

Multiplayer uses the existing online client, lobby, travel and prediction through
a bounded binary WebSocket transport and a same-origin gateway to registered,
trusted game servers. Each grant expires after one minute and can be used once;
account tickets are server-scoped. Origin, CSRF, private-master invite keys,
account bans, queue limits and connection limits are checked. Server passwords
remain normal game login options. No client-selected UDP address is accepted.

## Lessons

The wiki's `/wiki/vision-cancelling` and `/wiki/spy-grab` pages load the game on
demand. Their separate-tab links use `/play/?lesson=vision-cancelling` and
`/play/?lesson=spy-grab`.

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

Known limits:

- Forward+ has an upstream WebGPU texture-binding validation failure and draws a
  white world. Mobile is the working default. `?forward-plus` is diagnostic only.
- WebSocket uses TCP: packet loss can stall later frames, unlike native UDP.
  Microphone voice chat is currently disabled in browser visits.
- WebGPU instance uniforms did not reach actor shaders reliably in the pinned
  engine. Browser actor-local material uniforms carry lighting, heat, EMF, tint
  and foreground depth. Desktop keeps its normal instance path.
- The content subset contains TestRange, SteeE, Entry, Lobby, shared gameplay/UI/audio and default
  character assets. Cosmetic variants and other maps are excluded. The native
  EAX extension is not included; TestRange has no map reverb table.
- This is a local prototype branch. It has not been deployed or released.

Original converted content, engine packages, browser state, generated bundles,
logs and screenshots stay untracked. Console capture is restricted to loopback
hosts; hosted builds do not send console logs to the server.
