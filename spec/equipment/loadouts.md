# Default loadouts and inventory spawning

Units:
- uc:SBase.SBasePawn:UpdateInventory, uc:SBase.SBasePawn:ServerSpawnInventory, uc:SBase.SBasePawn:ServerSpawnWeapon,
  uc:SBase.SBasePawn:AddDefaultGadgets, uc:SBase.SPAWNDEFENSE:AddDefaultGadgets, uc:SBase.SPawnAttaque:AddDefaultGadgets
- uc:SBase.SGameInfo:AddDefaultInventory, uc:SBase.SGameInfo:ResetVisionMode
- uc:SBase.SPlayerProfile:GetGadgetRestriction, uc:SBase.SPlayerProfile:SetDefaultGadgets, uc:SBase.SPlayerProfile:SetGadgetsForTutorial,
  def:SBase.SPlayerProfile
- uc:SBase.SPlayerController:SpawnWeaponFor, uc:SBase.SPlayerController:MercWeaponIndex, uc:SBase.SPlayerController:ServerSetSpyInventory,
  uc:SBase.SPlayerController:ServerSetMercInventory
- uc:Engine.PersistentData:InitPersistentData (availability and restriction defaults), uc:Engine.PersistentData:Init,
  uc:SBase.SPersistentData:InitPersistentData
- uc:GUI.Loading_Menu:SetInventory, uc:GUI.Loading_Menu:BuildInventoryList, def:SBase.STeamAttaquant, def:SBase.STeamDefenseur, def:SBase.STeamInfo
- rep:SBase.STeamInfo.iNBGadgets, rep:SBase.STeamInfo.bDisallowedInv

## Purpose
What each side carries at spawn, where it comes from (player profile, defaults), how server restrictions replace
gadgets, and the order in which the inventory is created.

## Data
**DefaultProfile.ini** (System, section `[SBase.SPlayerProfile]`; class references are stored in the game's
annotated `Class'Pkg.Name'` form with name indices in parentheses):

| Key | Value |
|---|---|
| ATTEquipment[0..3] | SGadSmokeGrenade, SGadChaffeGrenade, SGadStickyCam, SGadHeartBeatSensor |
| DEFEquipment[0..3] | SGadFragGrenade, SGadMultiMinesLauncher, SGadCamNetwork, SGadGasMask |
| ATTWeapClass | SStickyShocker |
| DEFWeapClass | SWeaponDefense |
| SelectedMouse | 1 (mouse wheel toggles walk/run, not gadgets) |
| MouseSensitivity | 40.0 (class default 10.0) |

The SPlayerProfile class defaults and `SetDefaultGadgets()` give the same equipment and weapons.

Per-side default charges (live CDO): smoke 5, chaff 5, sticky cam 5, heartbeat -10 (unlimited); frag 4, mines 3,
cam network -10, gas mask -10. Plus, added for every merc: SGadTorche and SGadTorcheLaser (pad-up gadgets).

**Team options** (exact defaults, 8 per side, index = restriction bit):

| Bit | Spies (STeamAttaquant.ClassInventory) | Mercs (STeamDefenseur.ClassInventory) |
|---|---|---|
| 0 | SGadSmokeGrenade | SGadBackPack |
| 1 | SGadStickyCam | SGadGasMask |
| 2 | SGadChaffeGrenade | SGadMultiPresDetectorLauncher |
| 3 | SGadCloak | SGadCamNetwork |
| 4 | SSpyBulletLauncher | SGadMultiMinesLauncher |
| 5 | SGadNoiseEmitter | SGadFragGrenade |
| 6 | SGadFlashBang | SGadTazerLauncher |
| 7 | SGadHeartBeatSensor | SGadFlareLauncher |
| weapons | SStickyShocker | SWeaponDefense, SWeaponDefense_Uzi, SWeaponDefense_Shotgun |

`STeamInfo.iNBGadgets` default 4, `bDisallowedInv` default 0 but set by the host from
`PersistentData.b_SPY_GadgetRestriction` / `b_MERC_GadgetRestriction` (InitPersistentData: 255) and
`b_SPY_NBGadget` / `b_MERC_NBGadget` (4). Despite its name, a set bit in `bDisallowedInv` means the gadget is
**allowed** (`GetGadgetRestriction(i)` returns true for a set bit, and the gadget editor disables options whose bit
is 0). Availability flags from `InitPersistentData`: night, thermal, grab, elbow, merc EMF, motion, torch, laser,
berserk, charge true; both lethal drop techniques false.

## Rules
1. **Trigger.** `SGameInfo.AddDefaultInventory(pawn)` (server, when a player pawn is started) calls
   `UpdateInventory(GameMode)` then `AddDefaultGadgets(GameMode)`. `SGameInfo.ResetVisionMode` resets the goggles.
2. **UpdateInventory** is a reliable server -> owning client function: it runs on the player's own machine (on the
   host for the host's pawn) and reads that machine's profile (`GetPersistentData().PlayerProfile`).
   1. No profile: spawn the fixed defaults (spy: smoke, chaff, sticky cam, heartbeat, Shocker, and a bomb in
      Sabotage or "Multi" modes; merc: frag, mines, cam network, gas mask, rifle) and stop (no selection step).
   2. Tutorial map: the profile class defaults for the first `iNBGadgets` slots and the default weapon.
   3. Otherwise: for each of the 8 team options that is not allowed, any equipped slot holding it is replaced by the
      first allowed option not already equipped (scanning options 0..7), or cleared if none is left; this edits the
      in-memory profile. Then spawn equipment slots 0..`iNBGadgets - 1` that are not empty, then the profile weapon.
   4. `SelectGagdet(0)` and `Load()` it; spies in Sabotage or Multi also get a bomb; if the selected gadget is not
      activatable, is pad-up or is a bomb, `NextGadget()`.
3. **Spawning** goes through `ServerSpawnInventory(class)` and `ServerSpawnWeapon(class)` (reliable client -> server):
   the server spawns the item at the pawn and calls `GiveTo`. `ServerSpawnWeapon` first asks
   `SPlayerController.SpawnWeaponFor(class)`: in Merc Deathmatch it substitutes the player's chosen respawn weapon
   (first time: the team weapon matching the requested class, index from `MercWeaponIndex`, default index 0); in
   other modes it returns the class unchanged.
4. **AddDefaultGadgets**: merc: spawn SGadTorche and SGadTorcheLaser on the server and `GiveTo` (it is declared as a
   server -> owner function with a `bNetOwner` condition, which is still false on a pawn that has never been
   replicated, so it runs on the server); spy: nothing.
5. **Loading screen** (`GUI.Loading_Menu`): the client also sends its four gadgets and weapon to the server with
   `ServerSetSpyInventory` / `ServerSetMercInventory` (after applying the same restriction replacement), stored in
   `SpyGadgets`, `SpyWeapon`, `MercGadgets`, `MercWeapon`. No SBase script reads these fields in 4.0.
6. **Slice loadout** (Story on SteeE with default settings and the default profile): spy = Shocker, smoke (5),
   chaff (5), sticky cam (5), heartbeat sensor, night and thermal vision; merc = rifle (25 + 100), frag (4), mines (3),
   cam network, gas mask, torch, laser, motion and EMF vision. The plan's slice uses only spy night vision and smoke
   and merc torch, frag and motion vision; the other default gadgets still spawn and must exist (at least as stubs)
   for HUD and slot behaviour to match.
7. **The host's settings start from InitPersistentData.** `PersistentData.Init` (when the engine makes the object):
   `allowVoiceChatInLobby = true`, `friendMustBlink = false`, then `InitPersistentData()`: night and thermal vision,
   grab, both elbow attacks, EMF and motion vision, torch, laser, berserk and charge on; both lethal drop techniques
   off; both gadget restrictions 255 (every option allowed); both gadget counts 4. The SBase override then turns the
   cinematic camera off. The match creation pages call `InitPersistentData()` again before they apply their own
   choices. Login copies the counts and restrictions into the teams (`iNBGadgets`, `bDisallowedInv`), so a host
   whose settings never ran this gives every player 0 gadget slots: no gadget box, no Alt-fire use and no quick
   choice.

## States
None.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Slots per side | 4 | | decoded | STeamInfo, InitPersistentData |
| All gadgets allowed | 255 | bit mask | decoded | InitPersistentData |
| Team options | 8 per side | | decoded | ClassInventory |

## Network
- Server decides when (AddDefaultInventory); the client decides what (its own profile) and asks the server to spawn
  it. Items replicate back to the owner (`MyGadget`, `MyWeapon`).
- Because the client runs `SelectGagdet(0)` right after sending the spawn requests, on a remote client the list is
  usually still empty at that moment; the first gadget to arrive selects itself (`inventory-selection.md` Rule 2).

## Edge cases
- A client can request any class through `ServerSpawnInventory`; the server does not validate it against the team
  options or restrictions (the restriction pass runs on the client). OpenSCCT adds a server-side guard and a saved
  kit in place of the profile: see `spec/additions/gear-change.md`.
- With fewer than 4 slots configured, only the first slots are spawned; the rest of the profile is ignored.
- The no-profile path skips the selection and auto-advance step.

## Evidence
- System/DefaultProfile.ini; local/re-out/uc/SBase/SBasePawn.uc 3182-3352, SGameInfo.uc 526-530, SPlayerProfile.uc
  174-181 and 720-740, STeamAttaquant.uc, STeamDefenseur.uc, STeamInfo.uc, SPlayerController.uc 2229-2262 and
  8076-8092; GUI/Loading_Menu.uc 40-90, GUI/Edit_Gadgets.uc; Engine/PersistentData.uc 100-120.
- local/reflect/game.json: SBasePawn.UpdateInventory flags Net, NetReliable, Simulated (repOffset 65).

## Open questions
- Whether native or Reloaded code uses `SpyGadgets` / `MercGadgets` on the server (for example to validate or to
  respawn). Check Reloaded.Core strings and a trace of a remote client's spawn RPCs.
- The profile `MouseSensitivity` 40 to `fSensibiliteSouris` mapping (input spec).
