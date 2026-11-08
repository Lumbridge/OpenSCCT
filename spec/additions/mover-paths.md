# Native mover paths

OpenSCCT addition to the original mover; the decoded behaviour remains documented in `spec/devices/mover.md`.
Legacy maps continue using `KeyPos[8]`, `KeyRot[8]`, byte key indices and their original interpolation packing.

An optional `sim/mover-paths.json` (`svm.mover-paths/1`) carries complete relative poses and a starting stop for
individual movers. A native path has 1–65,536 stops; the upper bound guards resource usage. Stop offsets use world
axes relative to BasePos; rotations are rotator components added to BaseRot, as for legacy mover keys. Parsing
rejects invalid counts, starts, non-finite offsets, malformed vectors and duplicate actor ids. Names are matched
ignoring ASCII case. Map loading rejects missing actors and actors that are not movers.

```json
{"format":"svm.mover-paths/1","movers":[{"actor":"Platform0","start":0,"keys":[
  {"offset":[0,0,0],"turn":[0,0,0]},
  {"offset":[0,0,256],"turn":[0,0,0]}
]}]}
```

The loader installs native poses and the existing per-stop timing before BeginPlay. Native movers use integer
CurrentKey/PreviousKey and their path's KeyCount; legacy UC fields retain their original layout. Interpolation,
opening/closing, reversals, encroachment retries, lifts, rotating movers and match-size holds use the effective
indices and poses. Existing per-key timing/easing works across the whole path. SLift's own call-switch/door lists
still need to be configured as usual; a native path does not create landing infrastructure.

PathTarget and PathPrevious replicate as integer extensions on mover classes. The original SimInterpolate vector
continues carrying alpha/rate and compatibility indices; a client with a native path reads the integer extension
instead of the vector's byte packing. A key-index change also starts a new client leg when the compatibility
vector is unchanged. Both peers must use the updated network registry. Demo checkpoints keep the old mover record
for legacy maps and use a separately sized record with integer indices for native paths.

The editor writes this file when a path exceeds eight stops. Its gameplay.json retains only the legacy-sized
compatibility props; mover-paths.json is authoritative for native poses/count/start and is part of the simulation
hash. Older game builds do not support these paths. Original-game exporters must refuse them, not shorten them.

The tests use 300 stops to cross both the fixed-array limit and the byte boundary, exercise reversal, startup at
stop 280, timing, client interpolation, and end-to-end compile/import plus a host/client round at two latencies.
