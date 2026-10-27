# Mid-round gear change (an OpenSCCT addition)

The original game picks gear once, on the loading screen, from the player's profile (`spec/equipment/loadouts.md`).
This port has no profile, so before this addition every player always got the fixed side kit. The gear change lets a
player pick a kit at any time, with rules that keep it fair. Nothing here is a port: there is no `[PortOf]` and no
provenance tag. The numbers are our own and live in `OpenScct.Game.Equipment.GearK` and `OpenScct.Server.Hub.HubK`.

## Rules

1. **The saved kit.** The game client keeps the chosen kit in `settings.json` under `loadout`:
   - `spy_gadgets` and `merc_gadgets`, each 4 class paths ("" for an empty slot);
   - `merc_weapon`.

   The defaults are the fixed kits of loadouts.md rule 2.1. The game adds an `ILoadoutSource` to every level it
   loads (`GearServices.Install`, through `PlayMapLoader.AddLevelServices`).
2. **Every spawn reads it.** `Loadouts.UpdateInventory` runs on the player's own machine when the server asks for
   a kit (loadouts.md rule 2).
   - With no profile, the local player's kit comes from the saved kit.
   - The kit then goes through the host's restrictions and slot count, the same as a profile kit.
   - A weapon that is not in the team's list is replaced by the team's first weapon.
   - Remote players, bots and tutorial maps keep the fixed kit.

   So a kit saved at any time goes on at the next spawn, and the server keeps no queue.
3. **Asking for it now.** Apply in the picker saves the kit, then calls `SPlayerController.ServerChangeGear`. That
   is a reliable owner-to-server `CombatCall`. `SGameInfo.CanChangeGearNow` answers it:
   - **Match:** the kit goes on **now** only when all of these hold. Otherwise it waits for the **next spawn**.
     - the pawn is alive;
     - it spawned at most `GearK.SwapWindowSeconds` (10 s) ago (`LastStartTime`);
     - nothing of its kit has been used (`Loadouts.HasUsedKit`, see below).
   - **Hub** (`SHubGameInfo`): **now** while the player has a pawn, at most once every
     `HubK.GearChangeCooldownSeconds` (10 s). Otherwise the answer is **Cooldown**, sent with the seconds left.
4. **Nothing used** means all of these:
   - every gadget still has its class-default charge;
   - no gadget was used since the spawn (`LastUsed`);
   - the weapon's ammo and the rifle's clip are what they were when the server gave the weapon.

   The merc's torch and laser and the spy's bomb do not count.
5. **The swap.** The selected gadget is unloaded and the whole inventory deleted, as at a death. Then
   `AddDefaultInventory` asks the owner's machine for the kit again. The spawn time is kept, so a swap never
   reopens the window. Together with rule 4, this stops anyone from using a swap to refill.
6. **The answer.** The server sends `ClientGearChangeResult` with `Int1` = result and `Float1` = seconds left. The
   owner's HUD message area shows one of:
   - "Gear changed."
   - "Gear will change on your next spawn."
   - "Wait N s before changing gear again."
7. **The spawn-request guard.** The original spawns any class a client asks for, at any time (loadouts.md "Edge
   cases"). Now the server opens a grant when it asks for a kit (`Loadouts.OpenGrant`). For `GearK.GrantSeconds`
   (30 s) it takes only:
   - the team's slot count of gadgets, each an allowed team option the pawn does not already carry;
   - one weapon from the team's weapon list;
   - a spy's bomb in bomb modes.

   It refuses anything else without a message. Tutorial maps take any class. A team-less pawn takes up to 8 gadgets.
8. **The picker.**
   - It opens from **Change Gear** in the In-Game Menu (under Back; not on tutorial maps), or from the key bound to
     `ChangeGear`. That key is **I** by default: the stock layouts and the hub's keys leave it free, and it can be
     rebound.
   - Opened by the key, its Back and Escape close the whole menu.
   - It shows the player's side: one row per host slot, each cycling through the allowed options no other slot
     holds, or Empty. A merc also gets a weapon row.
   - A line under the rows states the rule.

## Constants

| Name | Value | Where |
|---|---|---|
| Swap window | 10 s | `GearK.SwapWindowSeconds` |
| Spawn-request grant | 30 s | `GearK.GrantSeconds` |
| Hub cooldown | 10 s | `HubK.GearChangeCooldownSeconds` |
| Picker key | I | `InputBindings.GearKey` |
