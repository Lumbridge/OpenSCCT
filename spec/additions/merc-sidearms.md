# Mercenary sidearms

OpenSCCT addition. A pistol is selected in a gadget slot but is carried as an independent weapon. Only one pistol
may be equipped; choosing another moves the displaced gadget to the old pistol slot. Existing kits do not gain a
pistol automatically. Gadget restrictions keep their original eight bits; pistols have no host restriction controls.

## Equipment and combat

`Sidearms` in Core defines stable IDs, names, unlock levels, magazine/spare ammunition, damage, cadence and reload
duration. Carry IDs are `OpenScct.Sidearm_<id>`. They remain in the four saved gadget choices; the server validates
the current level, mercenary role, one-pistol limit and remaining gadget grant before creating a carry item.

| ID | Level | Magazine + spare | Damage | Interval | Reload |
|---|---:|---:|---:|---:|---:|
| suppressed | 1 | 12 + 48 | 20 | 0.25 s | 2.0 s |
| automatic | 15 | 20 + 80 | 12 | 0.10 s | 2.3 s |
| magnum44 | 30 | 6 + 24 | 55 | 0.60 s | 2.8 s |
| desert_eagle | 45 | 7 + 21 | 50 | 0.40 s | 2.6 s |

Damage stays flat to 1000 uu and falls to half at 3000 uu; the automatic uses 500 and 2000 uu. Existing hit-zone
multipliers apply. Suppressed fire uses one-quarter gunshot radius and gameplay noise. The three semi-automatic
pistols need a fresh trigger press. The automatic repeats while held. Firing modes are fixed and pistols cannot snipe.

`MyWeapon` remains the active weapon. `PrimaryWeapon` and `SidearmWeapon` retain each gun and its ammunition.
`SwapWeapon` toggles the active slot, defaults to free G on keyboards and LB on fresh merc pad layouts, and is
rebindable. Existing bindings are preserved. A swap takes 0.5 seconds, cancels reload without refilling, and requires
releasing Fire. Blocking combat, gadget and incapacitation states refuse it; an existing scope closes first.

The carry item cannot be activated or shared. Cycling skips it, while numbered gadget selection preserves its slot.
Other gadgets operate with either gun drawn. Crates refill both guns; kit-use checks inspect both; cleanup removes
both guns and dummies. A locked saved pistol remains saved but grants no item until eligible.

The server checks weapon ownership, active slot, readiness, ammunition, shot sequence and cadence. Ammo revisions
reconcile prediction while retaining shots still awaiting acknowledgement. Runtime subclass default objects use their
actual class when replication shadows are initialized. Weapon actors, active slot and switch state replicate, including
to late joiners. The protocol hash rejects older registries.

## Progression and cosmetics

Public online access uses the verified current level; guests have the suppressed pistol. Offline/LAN and practice
allow all pistols. Prestige relocks access while preserving mastery, skin selections and owned finishes. Trusted
round-result responses refresh rank and cosmetic entitlements on the server before the following round.

Each pistol independently earns Night Ops at its unlock level +5 and Cryo at +10, using the highest level reached.
Mastery rewards are Woodland/Desert/Digital at 50/150/300 kills, Red Tiger/Blue Tiger at 75/200 headshot kills, and
Gold after all five milestones. Existing primary Gold requirements remain unchanged. Damage records the source
weapon so changing guns after hitting a victim does not change mastery credit.

Cosmetic slots 6–9 are the four pistols, in catalogue order. Existing IDs, bits and slots remain stable. `CosmeticLook`
retains the original 64-bit word and adds a second, producing up to 128-bit hexadecimal looks. Old looks still parse;
four PRI integers carry both words. Armoury, account profile choices, hub looks and previews retain individual finishes.

## Presentation and content

The first-person stance follows the requested CS2-style framing: both hands together on the grip, a forward-pointing
barrel in the lower right, restrained recoil, and a lowered draw transitioning into the ready stance. Authored
finger poses are fitted to each grip; contextual gadget and melee clips are retargeted onto the same rig. The inactive weapon is carried on the
holster bone. Revolvers eject no casing on firing and use a visible speedloader during reload.

`content/assets/sidearms/author.py` adapts four credited CC-BY 4.0 pistol assets, adds an original suppressor and
speedloader, and exports icons and editable Blender scenes. Original downloads and licenses remain in `sources/`,
and `CREDITS.txt` accompanies the shipped models. `svmbuild.pistol_arms` uses DJMaesen's credited, finger-rigged 2K
arms and authored pistol clips, retargets the converted merc contextual clips, and adds weapon mechanism and socket
tracks. Normal maps preserve glove, cloth and machined surface detail. Derived first-person animations stay in
the content output. The chars step generates 32 pistol finishes and matching sleeves for the existing merc finishes; the audio step generates eight
original synthesized firing/reload sounds. The release content builder includes the authored export inputs.

`play-sidearms` performs a rendered host/client pass through every pistol, Cryo finish, draw, fire, reload and return
to primary. Screenshots go under `SVM_PLAY_SHOTS`. This automated pass is separate from a player's manual assessment
of feel and animation quality.
