# Optional rifles and equipment levels

The **Additional rifles** host checkbox defaults to off, independently of Sidearms,
Practice and Alternate rules. It is available in Create Session, lobby parameters
and the hub laptop. `LobbySetup.Rifles` round-trips as `SvmRifles=0|1`, survives map
and mode changes, and becomes the replicated `GameReplicationInfo.RiflesEnabled`.
Local Play Level scenarios can opt in with `--svm-rifles` / `PlaySettings.Rifles`.

When disabled, the four additional rifles are absent from the Equipment menu and
the server rejects requests for them. When enabled, they appear after the stock
weapons in this order:

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
The host must still enable rifles, including in Practice.

Coverage: `EquipmentUnlockTests`, `RifleUnlockPickerTests`,
`RifleSessionOptionTests`, and the rendered `equipment-unlocks` smoke at
1920×1080 and 800×600.
