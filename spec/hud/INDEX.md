# HUD: spec index

[All specs](../README.md) · [Coverage report](../../docs/progress/COVERAGE.md)

Rules for the in-game HUD and how it follows the simulation. For the page player, drivers
and extension points, see the [HUD implementation guide](../../docs/HUD.md). Each spec lists
its coverage IDs in its `Units:` block.

| File | Covers |
|---|---|
| [screen-effects.md](screen-effects.md) | The canvas colour doubling; SHud screen effects (AddVisualSfx, ProcessSFX); damage feedback; the merc page's perturbation, shutdown, sticky shock, flash, foam and droplets |
| [poison.md](poison.md) | Poison gauge animation, textured screen overlay, activation frame and cure |
| [messages.md](messages.md) | Localized messages (SGameMessages, SDeathMessage, GameMessage), the four message areas, the game queue and the kill feed |
| [chat.md](chat.md) | Text chat: the typing line, the server's broadcast rules and the console lines |
| [merc-reticle.md](merc-reticle.md) | The merc's aim point (SpotReticule) and reticle by weapon, the sound arrows, the reticle message, the posture icon and the alert box |
| [counters-and-prompts.md](counters-and-prompts.md) | The alarm box, objective counter variants, countdowns, coop icons, the Speak prompt, the tutorial line, the respawn weapon |
| [spectator-page.md](spectator-page.md) | P_SpectatorMode, the page of a dead player or a spectator |
| [scoreboard.md](scoreboard.md) | The connected players list and the mission status page |
| [binoculars.md](binoculars.md) | P_BinocularsATT and P_BinocularsDEF, and the spy's objective markers in the binoculars |
| [cinematic-camera.md](cinematic-camera.md) | The Enhanced cinematic camera: the FreeCam and HideHud binds, the server permission and the free camera |
| [in-game-menu.md](in-game-menu.md) | The In-Game Menu (Escape, ShowMenu): its pages, keys, layout and the quit and lobby confirmations |
| [strategic-map.md](strategic-map.md) | The strategic map (the menu's button and F7, DisplayStrategicMap): floors, legend, the you-are-here and team-mate markers, UpdateMinimapParams |

## Relevant units not specced here

These allocation notes reflect the original research pass. Use the linked specs and
[coverage report](../../docs/progress/COVERAGE.md) to check later additions.

- The gadget overlays (sticky cam view and menu, camera network, mines, backpack, gas mask, heart sensor): the
  gadget area, through the HUD's area driver and page override hooks (docs/HUD.md).
- nat:SBase.SHud.HudAtt and nat:SBase.SHud.HudDef (the canvas parts other than the markers), the spy's interaction
  list: docs/HUD.md, from the Bridge HUD research.
