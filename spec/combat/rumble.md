# Controller rumble: the script's Vibration_* set

Units:
- uc:SBase.SPlayerController:Vibration_CheckSiPossible, uc:SBase.SPlayerController:Vibration_StopTout
- uc:SBase.SPlayerController:Vibration_CoupParDefaut, uc:SBase.SPlayerController:Vibration_SautMoyen,
  uc:SBase.SPlayerController:Vibration_SautHaut, uc:SBase.SPlayerController:Vibration_Bousculade,
  uc:SBase.SPlayerController:Vibration_Blast, uc:SBase.SPlayerController:Vibration_Grabbed,
  uc:SBase.SPlayerController:Vibration_Grab_Kill, uc:SBase.SPlayerController:Vibration_Charge_Course,
  uc:SBase.SPlayerController:Vibration_Charge_Obstacle
- the rumble shapes of uc:SBase.SPlayerController:Vibration_Tir, Vibration_Gadget and Vibration_StickyShock (the call
  sites are in merc-rifle.md, grenade-throw.md and sticky-shock-effect.md)

## Purpose

Which game events shake the controller, how hard, for how long, and which one wins when two overlap. Reloaded drives
the motors (`spec/native/reloaded-hooks.md` rule 28, FEATURES `rl:input.rumble`); the script picks the shape.

## Data

| Field | Owner | Notes |
|---|---|---|
| bVibrationActive | SPlayerController | script rumble on (NativeRumbleEnable; off at the end of a round) |
| iVibrationPriorite, fVibrationTempsLimite | SPlayerController | the running rumble's priority and end time |

## Rules

1. A rumble with priority P and length T starts only while script rumble is on, and only when the running one has
   ended (`Level.TimeSeconds` past its end) or P is higher than its priority; it then becomes the running one, ending
   at now + T. Stopping all rumble forgets the running one (priority -1, end 0) and stops the motors.
2. The shapes (priority; left low-frequency motor, right high-frequency motor, both 0 to 65535; seconds):

| Function | Priority | Low | High | Seconds | Raised by |
|---|---|---|---|---|---|
| Vibration_CoupParDefaut | 1 | 65535 | 32000 | 0.1 | damage of any other type; ClientShake with rumble |
| Vibration_Tir | 2 | 0 | 32000 | 0.12 | the merc's shot (also trigger motors 25000 each) |
| Vibration_Gadget | 2 | 0 | 19200 | 0.12 | a launched gadget (trigger motors 15000 each) |
| Vibration_SautMoyen | 3 | 65535 | 0 | 0.1 | not raised in Versus |
| Vibration_SautHaut | 3 | 65535 | 0 | 0.2 | a drop attack taken, a fall |
| Vibration_StickyShock | 5 | 20000 | 65535 | the freeze length | being sticky shocked |
| Vibration_Bousculade | 5 | 65535 | 0 | 0.35 | being charged or elbowed |
| Vibration_Blast | 7 | 65535 | 65535 | 0.35 | a blast |
| Vibration_Grabbed | 1 | 2000 + 0..5000 | 5000 + 0..10000 | 0.2 | each tick while held by a spy (random each time) |
| Vibration_Grab_Kill | 5 | 65535 | 0 | 0.2 | a grab or ledge kill begins |
| Vibration_Charge_Course | 1 | 40000 | 0 | 0.1 | the merc's charge, every tilt period |
| Vibration_Charge_Obstacle | 3 | 65535 | 0 | 0.2 | the charge or the berserk spin hits a wall or a player |

3. Rumble plays on the machine of the player it concerns only (all of these are simulated functions called on the
   owner; the charge's obstacle rumble reaches a remote merc through Client_Vibration_Charge_Obstacle).

## Rewrite

The simulation raises each call as a `CombatFx` notification named after the function (the stick shock's length as
its first parameter); `Options/PadRumble` keeps rule 1's priority and end time per player and starts Godot's joypad
vibration with the strengths of rule 2 scaled by the player's strength setting (the low-frequency motor is Godot's
strong magnitude). The trigger motors have no Godot call and are not driven.
