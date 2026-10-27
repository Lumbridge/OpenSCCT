# Reloaded.Core changes that touch networking

Units: rl:SBase.SPlayerController.MyServerMoveWithPCRot, rl:SBase.SBasePawn.ExecuteAction, rl:SBase.SPlayerController.CheckGEForWallJump, rl:SBase.SGadGrenLauncher.ThrowGren, rl:Engine.Actor.SetRotation, rl:Engine.Controller.GotoState, rl:SBase.SPawnAttaque.TickNative

## Purpose

Reloaded.Core.dll replaces 37 native thunks of the game at run time (`docs/spikes/R0-reflection.md`).
This file says which of them change what crosses the network or how the move protocol behaves, so
the port can reproduce the reference install (Enhanced 4.0 Beta 3, which ships Reloaded).

## Data

Each wrapper looks up the original native by its index in the engine's native table and calls it,
with changes before or after the call, except where it reimplements the native.

## Rules

1. **MyServerMoveWithPCRot (native 1392).** Server side: `MaxTimeMargin` is set to +infinity for the
   duration of the original call and restored afterwards. Effect: PCRot moves (Vandamme, back-to-wall
   fire, hand-over-hand pipe fire, `net-moves.md` rule 3.5) are never zeroed by the time-margin guard;
   `TimeMargin` keeps accumulating and is tested by the next plain move. No change to what is sent.
2. **ExecuteAction (native 1411).** Only for the local player's own pawn: around action 234 with
   `bIsLeftPart`, the controller's `fGEZLocation` is lowered by 46 and restored by 46 at the next
   action. A local traversal fix; nothing is sent. The engine's own action playback on proxies
   (`net-proxy.md` rule 8) and the server's execution of received actions call the engine code
   directly and do not go through this wrapper.
3. **CheckGEForWallJump (native 1363).** Sets the controller's `fGEZLocation` to −10 before calling the
   original. Local only.
4. **ThrowGren (native 1314).** Reimplemented: unless a Reloaded option is set, the launch location is
   rounded to whole units and each rotation component is cut to its high byte (a multiple of 256)
   before the grenade is spawned, which is what the values become when they cross the network
   (`net-replication.md` rule 17). A listen host's or standalone throw therefore matches what a
   remote client's throw produces on the server.
5. **SetRotation (native 299).** For the local player controller only, when called from two specific
   script functions, the rotation change is accumulated into Reloaded's own camera offsets and the
   controller's rotation is restored. Local camera behaviour; nothing is sent.
6. **GotoState (native 1175)** and **SPawnAttaque.TickNative (native 1414)** are reimplemented by
   Reloaded; they change state and pawn tick behaviour on every machine but not the protocol. They
   belong to the specs of those areas (`spec/reloaded/FEATURES.md`).
7. The other wrappers (LAN browser, chat, rumble, HUD, menus, `UdpLink.BindPort`, `FRand`,
   `PlayerController.Exit`) are lobby or local features outside the match netcode.

## States

None.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| MaxTimeMargin during PCRot | +infinity | s | decoded | Reloaded.Core wrapper of native 1392 |
| ExecuteAction height shift | 46 | uu | decoded | wrapper of native 1411 |
| CheckGEForWallJump preset | −10 | uu | decoded | wrapper of native 1363 |

## Network

Rule 1 is server side; rules 2 to 5 are local to the machine that calls them.

## Edge cases

- A port without Reloaded's PCRot change would reject PCRot moves of a fast client and correct it;
  the reference install never does.

## Evidence

- Wrapper addresses in Reloaded.Core (module-relative, from the transfer study's list of natives
  whose function pointer leaves the game image): MyServerMoveWithPCRot 0x10023870, ExecuteAction
  0x10023740, CheckGEForWallJump 0x10023930, ThrowGren 0x10023c80, SetRotation 0x10024180,
  GotoState 0x10024080, TickNative 0x10023710.
- Original game bodies: `execMyServerMoveWithPCRot` 0x10aaec30, `ExecuteAction` exe body 0x10ad6800.

## Open questions

1. Which Reloaded option disables the ThrowGren quantisation, and whether the reference install sets
   it.
