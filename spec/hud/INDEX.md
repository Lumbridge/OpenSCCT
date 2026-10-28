# HUD: spec index

Area: what the in-game HUD draws and how it follows the simulation. The page player, the page drivers and the
extension points are described in docs/HUD.md; the files below hold the rules ported from the decode. The exact unit
IDs of each file are in that file's `Units:` header.

| File | Covers |
|---|---|
| [screen-effects.md](screen-effects.md) | The canvas colour doubling; SHud screen effects (AddVisualSfx, ProcessSFX); damage feedback; the merc page's perturbation, shutdown, sticky shock, flash, foam and droplets |
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

- The gadget overlays (sticky cam view and menu, camera network, mines, backpack, gas mask, heart sensor): the
  gadget area, through the HUD's area driver and page override hooks (docs/HUD.md).
- nat:SBase.SHud.HudAtt and nat:SBase.SHud.HudDef (the canvas parts other than the markers), the spy's interaction
  list: docs/HUD.md, from the Bridge HUD research.
