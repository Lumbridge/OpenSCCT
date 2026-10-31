# Modern synchronization

OpenSCCT additions, estimated design. The decoded rules remain in move-protocol.md and proxy-pawns.md.

## Rules

1. Both rate presets use per-connection budgets. Gameplay simulation is fixed at 60 Hz.
2. A pawn snapshot is explicit evidence for that pawn at its source server tick. Silence or another actor's packet
   is not evidence of unchanged movement. Reject stale states before updating the latest authoritative values.
3. Presentation time is monotonic within a session. Adapt the buffer between two and six simulation ticks from
   observed packet lateness; change playback speed rather than jumping time. Extrapolate at most six ticks.
4. Pose records carry a server tick and reset identity. Complete channel records initialize a newly relevant pawn;
   channel changes are ordered and reliable, while phase samples and transforms may supersede older samples.
   A bounded authored-transition journal preserves successive plays and clears within a tick. A completed-tick
   phase sample wins over an intermediate descriptor at the same tick, regardless of arrival order.
   Remote presentation must not execute damage, interactions, root-motion physics or gameplay animation notifies.
   Snapshots include four preceding phase ticks. Linear playback is reconstructed; deviations, rate changes, blends
   and revision boundaries are encoded explicitly. A newer transform alone does not prove that display time has
   resolved clip descriptors or animation samples.
   Cooperative readiness and grab/termination eligibility share this timeline; proxy presentation cannot depend
   on executing native gameplay actions to reconstruct those flags. Repeated host-tick phases, shared locomotion
   cycles and common descriptor scalars have compact encodings without dropping required transitions.
   Reliable body-state keys preserve stance, physics, visibility, attachment and paired-reference boundaries between movement
   snapshots. Their sequence detects missing state; older timestamped keys fill the bounded motion history without
   moving its newest receive reference backward. Buffered actor codes resolve again when late actors become known.
   Remote proxies withhold legacy properties already represented by this presentation timeline.
5. Owner movement history holds 256 ticks, reserving four command slots per tick for physics splits. More splits
   or an unacknowledged input older than that window requests a clean reset. Commands include an explicit input
   tick; pre-controls, physics and post-controls share its clock. Commands and acknowledgements identify the same simulated input.
   Anchor a lifetime's input ticks to the host clock with a two-tick playout cushion. Ordinary arrival jitter must
   not turn constant input into alternating zero/two-step authority poses. Outage catch-up remains bounded.
   Reconciliation restores authoritative movement and replays remaining inputs with presentation and network
   effects suppressed. Reset identities separate possession, respawn and forced reinitialization.
   Routine native mirrored-state acknowledgements do not reset the command sequence. An owner reset identifies
   the pawn lifetime explicitly; stale commands for another possession cannot enter the new simulation.
   Acknowledgements also report buffered receipt separately from processed input. Redundant sends rotate through
   missing inputs around the oldest gap instead of waiting a round trip to advance a fixed recovery prefix or
   cycling through all newer inputs after an outage. Controls retain before-step
   and completed-step reliable fences: animation-end actions are flushed before the tick is sealed and execute
   after its physics. Multiple commands in one tick retain their individual boundaries.
   Recorded collision inputs include other pawn cylinders and moving-base poses. Temporarily stage these for each
   replay step, carry the owner through base translation/rotation, and restore live actors before current contacts
   are reconciled. Bump, attach and detach gameplay callbacks cannot repeat during replay. Corrections include the
   authoritative base pose so placement can be reconstructed in the acknowledged input's base coordinates.
   Actions authored externally on an owned pawn use a reliable ordered owner stream keyed by pawn, movement epoch,
   source tick and sequence. Scope the originating owner across pre-controls, physics and post-controls, so its
   predicted actions are not echoed while partner actions still arrive. Older correction context cannot overwrite
   a newer received authoritative action.
   Dead pawns advance on the authority's world clock. Do not record untransmitted owner heartbeats while they wait
   through their death sequence; a new possession starts a fresh command lifetime.
6. A teleport clears interpolation and rewind history. A reset cannot be interpolated into an earlier lifetime.
7. Hit rewind defaults to 300 ms. Invalid or future view times cannot select a historical pose. The recorded
   history and the configured ceiling bound valid rewinds.
8. Simulated links can vary by directed endpoint pair. Burst loss and asymmetric conditions are deterministic,
   use the injected clock and seeded random stream, and preserve the existing symmetric profile semantics.
   The transit pool is reserved before stepping and bounded; exhaustion drops and counts datagrams deterministically.

## Acceptance

Test 100, 150 and 200 ms RTT with jitter, loss, duplication and reordering, plus 300 ms stress and outages.
Compare poses and transforms at their source time, not simultaneous positions from machines at different times.
Check spawn/relevancy during traversal, root motion, paired actions, multiple changes in one receive pass, stale
data after resets, idle and menu corrections, moving bases, packet limits and allocations. Rendered verification
is required in addition to simulation assertions; record skipped or unavailable checks explicitly.
