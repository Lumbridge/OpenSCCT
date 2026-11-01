# Controlled network game testing

`tools/network-lab.ps1` launches independent game processes using the regular session or Hub code, with
isolated settings and loopback UDP. `-Dedicated` adds a separate server executable and makes the host-view
client connect over UDP too. Rendered runs use `-Windowed` but stay off screen without taking focus.
These tests do not establish physical PC/laptop or Wi-Fi behavior.

The lab is opt-in through `SVM_NETLAB_DIR`; its command files are not a network administration API.
Accounts, master registration, UPnP and production services are not required. The default content root is
the checkout's `.content`. Build both solutions before a final acceptance run. `-SkipBuild` is for a build
already verified in this checkout; avoid rebuilding a running dedicated server executable on Windows.

## Runs

```powershell
# Separate rendered host/client; hold the guest's scene load for 20 seconds.
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario startup -LoadDelayMs 20000 -Windowed

# Remote spy drops from the Aquarius Seaside ledge under impaired UDP.
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario ledge -ReverseRoles -Windowed -NetworkProfile rough

# Independent observer checks a grab, knockout and wake-up.
pwsh tools/network-lab.ps1 -Action Run -Dedicated -Map AquaE -Scenario grab -Observer -Windowed -NetworkProfile asymmetric

# Spy-camera gas received and rendered by both players, with expiry and dose accounting.
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario sticky-gas -Windowed -NetworkProfile typical -HostFps 144 -GuestFps 30

# Merc camera entry, idle/turning stability, switching, disabled overlay and exit.
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario camera-network -Windowed -NetworkProfile lan

# Lethal Use release or the original hold timeout, followed by spectator delay and respawn.
pwsh tools/network-lab.ps1 -Action Run -Dedicated -Map AquaE -Scenario grab -GrabOutcome neck-break -Lives 2 -Observer -Windowed -NetworkProfile asymmetric
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario grab -GrabOutcome timeout -Lives 2 -Observer -Windowed -NetworkProfile rough

# Rifle input, displayed target stance, damage, shot/ammo agreement and reload.
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario fire -TargetStance crouched -ReverseRoles -Windowed -NetworkProfile asymmetric
pwsh tools/network-lab.ps1 -Action Run -Dedicated -Map AquaE -Scenario fire -FireSeconds 1 -Lives 2 -Windowed -NetworkProfile rough

# Dead remote spy watches two teammates, then one, then the final result.
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario spectator -Lives 1 -SecondObserver -ObserverTeam 0 -ReverseRoles -VictimRole guest -Windowed -NetworkProfile rough

# The last survivor leaves instead of dying. "first" leaves the first watched teammate.
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario spectator -Lives 1 -SecondObserver -ObserverTeam 0 -ReverseRoles -VictimRole guest -SpectatorDisconnect last -Windowed -NetworkProfile rough

# Remote merc dies crouched, with a teammate available during the respawn delay.
pwsh tools/network-lab.ps1 -Action Run -Dedicated -Map AquaE -Scenario death -Lives 2 -Observer -ObserverTeam 1 -VictimRole guest -DeathStance crouched -Windowed -NetworkProfile rough

# Actual airborne damage; cannot be combined with crouched death.
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario death -Lives 2 -VictimRole guest -AirborneDeath -Windowed

pwsh tools/network-lab.ps1 -Action Run -Hub -Scenario rejoin -Windowed -NetworkProfile typical
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario roundtrip -Lives 1 -Windowed
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario soak -SoakSeconds 3600 -HostFps 144 -GuestFps 30 -Windowed -NetworkProfile limited
```

Supported scenarios also include `jump`, `stall`, `outage`, `stance` and `elimination`. Hub rules disable
combat; grab/death/elimination tests belong in a regular session. Dedicated Hub mode is not implemented.
The host defaults to spy and guest to merc; `-ReverseRoles` swaps them. Observers are playing clients, so
their team and personal lives affect the match. An actual visitor spectator has separate integration coverage.
`-SecondObserverTeam` optionally gives the second observer a different side; its default `-1` inherits
`-ObserverTeam`. With the normal host/guest split, `-ObserverTeam 0 -SecondObserverTeam 1` creates a balanced
two-versus-two match. The runner rejects an exported build that ignores this explicit second-observer side.

`load-timeout -LoadDelayMs 75000` observes a regular-session guest beyond the sixty-second load deadline.
It requires live authority samples proving the early match stayed gated, removal of the failed connection,
the ready host proceeding, and no playable guest after the late readiness attempt. It waits another
twenty-five seconds after the injected stall for the client's normal connection-error path; a buffered
packet can defer its fifteen-second idle timeout when the main thread resumes.

The `spectator` scenario requires two observers on the victim's team and one personal life. It checks
authority, victim target, displayed watched name, personal/team lives, and every remaining client's final
winner/reason. The `death` scenario checks corpse position/animation agreement, exactly-once life/death
accounting and respawn or elimination. Its `damage` command is an explicit fixture using the real damage
handler; it does not prove a weapon can hit that target. Grab input goes through the normal Use/fire bindings.
The lethal grab variants require at least two lives, verify one death/life and one credited kill, detached
corpse position, a spectator interval and a new playable pawn. The default grab outcome remains knockout.
The knockout outcome additionally requires the merc's mask to remain off and their weapon to be loaded
and drawn after waking, then checks normal firing and grenade use. The `sticky-gas` scenario uses an
explicit collision-checked camera placement fixture and normal release input; it checks replicated gas
actors and rendered particle counts. The `camera-network` scenario uses a disable-camera fixture for
its final overlay capture. These two scenarios currently require the default spy host / merc client setup.
The `fire` scenario taps the normal auto-mode trigger once, or holds it for `-FireSeconds` (up to two).
Single fire mode is reserved for the original scope behavior. Target placement/aim are explicit fixtures;
firing and reloading use bound inputs. This proves those actions for the recorded pose/profile, not the
entire moving-target/headshot/cover-rewind matrix.

## Network conditions

Only the guest traverses the fault-injection proxy; other clients provide a healthy-peer comparison.
The proxy changes UDP delivery, including connection and reliable traffic, without reading game payloads.
Randomness uses `-Seed` (default 119). Process scheduling still affects datagram ordering, so a seed alone
does not recreate an identical trace.

| Profile | Up / down delay | Per-direction jitter | Loss | Duplication | Rate limit |
|---|---:|---:|---:|---:|---|
| direct | none | none | none | none | none; bypasses proxy |
| lan | none | none | none | none | none |
| typical | 40 / 40 ms | ±10 ms | 1% | 0 | none |
| rough | 100 / 100 ms | ±30 ms | 3% | 0.5% | none |
| asymmetric | 40 / 120 ms | ±20 ms | 2% | 0.5% | none |
| limited | 100 / 100 ms | ±30 ms | 3% | 0.5% | 5,000 B/s up, 12,000 B/s down |

Jitter can reorder packets. The limited profile bounds its queue to two seconds; excess datagrams are
dropped. `outage` injects three seconds of loss; `soak` injects one second every ten cycles. Each soak cycle
jumps, lands, crouches, moves forward/backward, stands and settles. Its checkpoint requires walking,
owner/authority position agreement within 3 uu, zero unexpected resynchronizations and fewer than 60
outstanding inputs. These are checkpoint limits, not a guarantee about every frame's latency or pose.
Current runs also retain each other player's replica at those settled checkpoints: its pose must be ready,
its stance/physics must match authority, and its position must agree within 3 uu. Missing participants fail
the check. Older run records without `replicas` have no such acceptance evidence; the index reports zero
replica samples and no maximum, rather than inventing coverage. This resting comparison is appropriate
after the three-second settle; moving remote poses must instead be compared at their displayed history tick.

The soak drives every participating client, including `-Observer` and `-SecondObserver`. Use
`-Hub -SecondObserver -Scenario soak` for four active Hub participants; the normal Hub capacity is 50,
so this does not establish full-capacity behavior. Hub always loads the `Hub` map. New manifests record
that name; the index preserves older ignored map arguments as `recordedMapArgument`.

The strict rendered `stance` floor check is separate. Existing transient toe-depth failures are retained
in the overnight report; do not replace its tolerance with the observed failure value to obtain a pass.

## Manual control and evidence

The `stale-combat` scenario holds guest-to-server datagrams for eleven seconds across a real death and
respawn, then checks that old self-damage cannot consume another life and that a fresh request still works.
Use `-NetworkProfile lan -Lives 3 -RespawnSeconds 1`; add `-ReverseRoles` to make the guest a spy. This uses
explicit damage fixtures, not player combat input. The delay exceeds retained movement history, so its
documented recovery may replace a pawn without consuming a life. The result records that separately.
The proxy's local control file accepts `upDelayMs` from 0 to 14,000; queues retain their existing bounded limit.

```powershell
pwsh tools/network-lab.ps1 -Action Start -Map AquaE -Windowed -NetworkProfile rough
pwsh tools/network-lab.ps1 -Action Send -RunDirectory <printed-directory> -Role guest -CommandJson '{"op":"input","steps":[{"label":"jump","seconds":1,"tap":["Jump"]},{"label":"land","seconds":4}]}'
pwsh tools/network-lab.ps1 -Action Send -RunDirectory <printed-directory> -Role guest -CommandJson '{"op":"capture"}'
pwsh tools/network-lab.ps1 -Action Status -RunDirectory <printed-directory>
pwsh tools/network-lab.ps1 -Action Stop -RunDirectory <printed-directory>
python tools/network-lab-report.py --since 20261101 --output local/network-lab/evidence-index
```

Runs live under `local/network-lab/<timestamp>-<id>`. `Run` stops its owned processes in `finally`; `Start`
requires `Stop`. Cleanup verifies PID and process start time, avoiding unrelated game processes.

- `run.json`: process ownership, topology, profile/seed/FPS, scenario options, commit/dirty status and
  source, assembly and content hashes. Older runs may lack some scenario options; keep their command log.
- `config.json`: game-side session setup and sampling interval.
- `result.json`: conclusive scenario pass/fail and error. Its absence means incomplete evidence.
- `*-status.json`, `*-trace.jsonl`: each client's state plus authority samples collected on the server thread.
  Use player names/pawn lifetimes across worlds, not local actor IDs.
  Client online errors and the authority's last 16 disconnect reasons distinguish transport rejection from scene travel.
- `*-acks/`: command completion or rejection. Placement and damage fixtures remain distinguishable from input.
- `shots/`, `*.log`: rendered captures and process output. Inspect images before making visual claims.
- `soak-progress.jsonl`: completed checkpoints; an interrupted/failed attempt does not become a successful hour.
- Scenario-specific result files retain the relevant targets, corpse states, results or startup evidence.

The report command writes a JSON index with full evidence paths/hashes and a compact Markdown table. It
accepts `--executable <path-to-OpenSCCT.console.exe>` to include only that client candidate, which prevents
earlier passing runs from being counted as acceptance of a newer build. Without that filter it explicitly
labels the index as potentially containing multiple build identities. It
marks missing results `INCOMPLETE`, including manual runs, and never assumes those processes are still alive.
Its maxima cover recorded successful checkpoints; inspect the failed interval's trace/error separately.
The independent visual-flags column preserves final inspector findings even when the scenario passes.
Earlier scene inspections remain in each trace. New diagnostics also retain the channel/position sample
that produced each minimum/maximum foot measurement. A movement soak rejects missing/frozen animation
issues, but does not silently turn its separate known floor-alignment failures into visual passes.
Animation windows measure every drawn sample against the starting pose. Comparing only the two endpoints
would misclassify a complete looping gait as frozen. A respawn must retain its fresh healthy pawn, playable
state and own-pawn camera for a further two seconds; the complete authority trace must also contain no
healthy pawn in death or killed-spectator state. The dedicated helper records state-changing received RPCs
in `received-state-calls.jsonl`, including rejected death requests, for diagnosing lifetime/order failures.
The floor inspector separately records a draw still interpolating from Falling into Walking. It retains
that transition's height and channels, without counting it as a fully grounded sample. The same strict
8 uu penetration limit applies once both render endpoints are grounded.

The overnight report is [NETWORK-OVERNIGHT-REPORT-2026-11-01.md](NETWORK-OVERNIGHT-REPORT-2026-11-01.md).
Physical-machine acceptance should use the same tested client build on both PCs because the movement
protocol handshake rejects older formats.

Use `-GameExe <absolute-path-to-OpenSCCT.console.exe> -SkipBuild` to test an exported game. This omits
`--path game`, so the run loads the export's pack and assemblies. Copy `game/override.cfg` beside that
executable before windowed runs, preserving the checkout's no-focus test policy. The manifest records the
exported executable, pack and actual assembly hashes; the evidence index labels exported runs separately.

Use `-DedicatedExe <absolute-path-to-OpenScct.NetworkLab.Server.exe> -SkipBuild` to pair an export with
an isolated helper build. This allows an older soak to finish without overwriting its running server,
and avoids accidentally pairing a new client protocol with an older helper. For example, build the
helper with `dotnet build tools/network-lab-server/OpenScct.NetworkLab.Server.csproj -o local/my-server`.
The dedicated lab helper, when requested, still uses its explicitly recorded separate server binary.

The `gadget` scenario throws the remote merc's normal frag grenade through bound alternate-fire input.
It checks one authoritative projectile, one observed lifetime on each nearby client, fuse cleanup and an
exactly-once charge decrement. The current gameplay registry retains grenade channels; the comparison uses
the controlled single-throw interval, type/owner and per-world actor lifetimes so older failed runs remain comparable.
`-GadgetDownBlackoutMs 1000` drops the opening's downlink delivery and requires its recovery.
`-GadgetDownBlackoutMs 3500` covers the entire authoritative fuse: the healthy observer must see it, while
the impaired guest must not retain a stale projectile after recovery. The proxy records when the directed
blackout ends; the fixture rejects runs where the grenade actually lived beyond that outage.

## Named acceptance matrix

`tools/network-lab-matrix.ps1 -List` lists the repository's named cross-map cases from
`tools/network-lab-matrix.json`. Run all cases, or choose `-Case steel-movement,club-airborne-merc-death`.
Pass `-GameExe <path> -DedicatedExe <path> -SkipBuild` to reuse a prepared exported candidate and isolated dedicated helper; otherwise
the runner builds once before starting. Do not rebuild a dedicated helper that is already running.
`-DryRun` prints resolved case parameters without launching anything. `-MaxMinutes` prevents starting
another case after that duration; an already running case still finishes with its normal deadlines.

Cases run serially. A failed case is retained and the remaining cases continue. The matrix's own directory
contains each console log and an updated `matrix-result.json`; individual run directories remain discoverable
by the evidence index. Any failed, incomplete or budget-skipped case makes the matrix command fail. A killed
runner leaves an explicit `INCOMPLETE` entry; it does not infer a pass from partial checkpoints. The default
matrix includes the strict stance/floor check: its known unresolved penetration is an ordinary failure,
not an expected-failure exemption. The matrix supplements the longer soaks and is not the complete plan.
