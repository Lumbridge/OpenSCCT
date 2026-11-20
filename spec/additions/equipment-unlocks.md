# Optional rifles and equipment levels

New matches enable additional rifles and sidearms by default. The creation and lobby menus use these standard rules.
The server carries `SvmRifles` and `SvmSidearms` in the round and replay options through map and mode changes.
A custom server can still set either rule to zero. Its equipment menu and grant checks honor the restriction.

The following items have level requirements.

| Equipment | Required level |
| --- | ---: |
| Galil | 1 |
| FAMAS | 5 |
| AK-47 | 20 |
| M4A1-S | 30 |
| Mine Hack | 5 |
| Spy Clone | 10 |

Locked equipment remains visible but grey, cannot be selected, and displays its
required level on its tile and in the details. Saved locked weapon choices fall
back to the stock rifle; locked gadget choices are replaced with an available
gadget in the menu. The server checks grants independently of the menu, including
Mercenary Deathmatch's respawn weapon selection.

Levels use the current rank, so prestige relocks equipment. Like existing sidearm
rules, offline/practice and authenticated unrestricted sessions bypass levels.
Custom servers can also restrict equipment through their match rules.

Coverage: `EquipmentUnlockTests`, `RifleUnlockPickerTests`,
`RifleSessionOptionTests`, and the rendered `equipment-unlocks` smoke at
1920×1080 and 800×600.
