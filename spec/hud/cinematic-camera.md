# Cinematic camera: Free Camera and Hide HUD (Enhanced 4.0 with Reloaded)

Units: uc:SBase.SPlayerController:FreeCam, uc:SBase.SPlayerController:HideHud,
uc:SBase.SPlayerController:CinematicCameraGranted, uc:SBase.SPlayerController:ServerRequestFreeCam,
uc:SBase.SPlayerController:ServerRequestHideHud, uc:SBase.SPlayerController:ClientStartFreeCam,
uc:SBase.SPlayerController:ClientToggleHideHud, rl:SBase.SPlayerController.ToggleFreeCamera,
rl:SBase.SPlayerController.ToggleHideHud

## Purpose

The Enhanced 4.0 "Cinematic Camera" binds (`enh:ui.cinematic`): a living player can fly a free camera away from
their pawn, and hide the HUD, when the host turned the camera on. It is meant for shots and streaming. The script
half (the binds and the server round trip) is in SPlayerController; the camera and the HUD switch are Reloaded
natives 1900 and 1901 (spec/native/reloaded-hooks.md rules 24 and 25).

## Data

| Field | Owner | Notes |
|---|---|---|
| bAvlb_CinematicCamera | SPlayerController, GRI | the host's switch; `ClientSetCinematicCamera` sets the controller's copy at login and every client PlayerTick copies it into its GRI |
| bAlternateMode | GRI | replicated; the natives also need it (except offline) |
| FreeCameraFlying | SPlayerController | non-zero while the free camera flies; this machine only, never replicated |
| the free camera's spot, rotation and speed | the player's own machine | the rewrite keeps them on the controller (`FreeCamLocation`, `FreeCamRotation`, `FreeCamSpeed`) |
| the hide-HUD switch | the player's own machine | a Reloaded flag of its own, separate from `HUD.bHideHUD` (the rewrite: `bCinematicHideHud`) |

## Rules

Who may use it:

1. `CinematicCameraGranted()` is true when the controller has a GRI whose `bAvlb_CinematicCamera` is set.
2. The natives test more: offline (standalone) always passes; otherwise the GRI needs `bAlternateMode` and
   `bAvlb_CinematicCamera`. In both cases the player needs a camera and a living pawn. So it is for living players,
   at any time in the round; a dead player or a spectator cannot start it (they already have their own cameras).
   The host's server builds the GRI's flag as Alternate Mode and the Cinematic Camera switch together.

The binds:

3. **FreeCam** (exec). While the free camera flies, it stops it here at once (no server round trip). Otherwise, when
   not granted, the console says "Free look is not enabled on this server."; when granted it calls
   `ServerRequestFreeCam`.
4. **HideHud** (exec). When not granted the console says "Hiding the hud is not enabled on this server."; else it
   calls `ServerRequestHideHud`.
5. `ServerRequestFreeCam` / `ServerRequestHideHud` (client to server, reliable) check `CinematicCameraGranted()` on
   the server's own GRI and, when it passes, answer with `ClientStartFreeCam` / `ClientToggleHideHud` (server to
   client, reliable). The answers call the natives `ToggleFreeCamera` / `ToggleHideHud` on the player's machine. A
   modified client that skips its own test is still refused by the server's test.

The free camera (`ToggleFreeCamera`, native 1900):

6. Starting (permission rule 2): the current view spot (`vCamLocation`) is saved, the view goes to the free camera
   (the script's `ClientSetViewTarget`), behind view is turned off unless the player is on team 1 (the mercs, who
   already see in first person), both camera speeds become 600 uu/s and the pawn's rotation, `IK_Type` and the time
   are remembered. Called again, it gives back the saved view.
7. While it flies the pawn stays where it was: the global PlayerTick clears bFire, bAltFire, bUsingNow, TimerUSe and
   fThrowForce every frame (input-and-rotation.md frame order rule 1), so the pawn cannot shoot or use anything.
8. The controls (Enhanced 4.0 key rows): the movement keys fly it, Space and Ctrl go up and down, the mouse wheel
   sets the speed, Shift is fast and Alt is slow; on a pad the left stick moves, the triggers go up and down and the
   shoulder buttons set the speed. The camera passes through walls.

Hide HUD (`ToggleHideHud`, native 1901):

9. With the same permission test as rule 2 it flips the hide-HUD switch; the HUD is not drawn while it is on. It is
   independent of the free camera: either can be on without the other.

### The rewrite (estimated where the native was not decoded)

10. The free camera is the controller itself: `ViewTarget` is the controller while it flies, so the first-person
    weapon is not drawn and the player's own body is (the play view only hides the own pawn while not flying). The
    calc view returns the free camera's spot and rotation and keeps them in `vCamLocation` and `rCamRotation`.
11. Each local tick, before PlayerTick, the free camera reads the input the input layer stored on the controller:
    yaw and pitch turn by `aTurn` and `aLookUp` at the merc's mouse rate (input x sensitivity x dt x 50), pitch
    held within +-16000; forward and back along the view, left and right flat, up with the Jump axis (aUp > 0)
    and down with Crouch. It then clears the controller's inputs (stick, mouse, every input byte), so whatever state
    the controller is in, the pawn stands still and does nothing; while flying only the FreeCam and HideHud commands
    run. Speed: 600 uu/s, x1.25 or /1.25 per wheel step within 75 to 4800, x2 with Shift, x0.25 with Alt.
11a. On a pad (rule 8): the left stick flies it and the right stick turns it as they move and turn the pawn; the
    right trigger goes up and the left trigger down (analog, added to the Jump and Crouch keys, capped at full
    speed); each press of the right shoulder button is one speed step up, the left one step down. While it flies the
    triggers and shoulder buttons do nothing else.
12. The free camera stops by itself (the saved view target and behind view come back) when the pawn dies or goes.
13. The hide-HUD switch hides everything the HUD's `bHideHUD` hides, and the merc's aim point too (also hidden while
    the free camera flies, since the view is not the merc's).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Free camera speed | 600 | uu/s | decoded | Reloaded 0x10022f00 |
| Wheel speed step | x1.25 | | estimated | ours |
| Speed limits | 75 to 4800 | uu/s | estimated | ours |
| Shift / Alt | x2 / x0.25 | | estimated | ours |
| Pitch limit | +-16000 | rotator units | estimated | ours |
| Messages | "Free look is not enabled on this server.", "Hiding the hud is not enabled on this server." | | decoded | SPlayerController.FreeCam, HideHud |

## Network

`ServerRequestFreeCam` and `ServerRequestHideHud` go client to server (reliable, `Role < Authority`);
`ClientStartFreeCam` and `ClientToggleHideHud` go server to client (reliable, `Role == Authority`); rep-controller.md.
The camera and the HUD switch themselves live only on the player's machine: other players see the pawn standing
still. The server's relevancy still sees from the pawn's camera (the controller's spot that `UpdatePositionForServer`
sends), so actors far from the pawn may not be replicated to a free camera that flew away.

With a listen host and a client: the host's own player runs the whole chain on the server (Role is Authority, the
answers run locally because the player is not remote). A client's request crosses to the server, is checked against
the server's GRI and the answer comes back to the client's controller, where the camera starts.

## Edge cases

- Stopping the free camera never asks the server, so a camera started before the host turned the switch off can
  still be stopped.
- The script test (rule 1) and the server test only look at `bAvlb_CinematicCamera`; Alternate Mode is only tested
  by the natives (rule 2). The rewrite's server builds the GRI flag as Alternate Mode and the switch together, so
  both tests agree there.

## Evidence

- local/re-out/uc/SBase/SPlayerController.uc: FreeCam, HideHud, CinematicCameraGranted, ServerRequestFreeCam,
  ServerRequestHideHud, ClientStartFreeCam, ClientToggleHideHud, ClientSetCinematicCamera, PlayerTick (the
  FreeCameraFlying clears); the replication block.
- Reloaded.Core v4.0d ToggleFreeCamera 0x10022f00, ToggleHideHud 0x10022e70 (spec/native/reloaded-hooks.md).
- Enhanced 4.0 GUI.int `[ExtraKeyRows]` Cinematic Camera section (the controls).

## Open questions

1. The free camera's own tick inside Reloaded (turn rate, how the wheel and Shift/Alt scale the speed, collision)
   was not decoded; rule 11 is the rewrite's estimate.
2. Whether Reloaded stops the free camera when the pawn dies (rule 12 is ours).
3. The pad mapping of rule 11a (which trigger is up, one speed step per shoulder press) is ours; Enhanced's exact
   pad code was not decoded.
