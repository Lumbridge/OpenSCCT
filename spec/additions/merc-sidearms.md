# Mercenary sidearms

OpenSCCT addition. Merc loadouts have Primary Weapon, Sidearm and Gadgets sections. One pistol occupies a dedicated
sidearm slot without consuming a gadget slot. New player kits default to the level-1 unsuppressed pistol. Old saved
pistol choices migrate out of gadget slots, restoring a missing default gadget. Gadget restrictions keep their
original eight bits; pistols have no host restriction controls.

## Equipment and combat

`Sidearms` in Core defines stable IDs, names, unlock levels, magazine/spare ammunition, damage, cadence and reload
duration. Carry IDs are `OpenScct.Sidearm_<id>`. The saved `merc_sidearm` choice is independent of the four gadget
choices; the server validates current level, mercenary role and a separate one-pistol grant. A locked selection
falls back to the level-1 pistol in the picker.

| ID | Level | Magazine + spare | Damage | Interval | Reload |
|---|---:|---:|---:|---:|---:|
| unsuppressed | 1 | 12 + 48 | 20 | 0.25 s | 2.0 s |
| automatic | 15 | 20 + 80 | 12 | 0.10 s | 2.3 s |
| suppressed | 25 | 12 + 48 | 20 | 0.25 s | 2.0 s |
| magnum44 | 30 | 6 + 24 | 55 | 0.60 s | 2.8 s |
| desert_eagle | 45 | 7 + 21 | 50 | 0.40 s | 2.6 s |

Damage stays flat to 1000 uu and falls to half at 3000 uu; the automatic uses 500 and 2000 uu. Existing hit-zone
multipliers apply. Suppressed fire uses one-quarter gunshot radius and gameplay noise. The three semi-automatic
pistols need a fresh trigger press. The G18 defaults to automatic fire and toggles between single shot and automatic
using the existing Switch Fire Mode action. Single shot requires a fresh trigger press; automatic repeats while held.
The selected mode survives weapon swaps, replicates to late joiners and appears on the HUD. Other pistols retain
fixed single-shot fire. Pistols cannot snipe.

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

Public online access uses the verified current level; guests have the unsuppressed pistol. Offline/LAN and practice
allow all pistols. Prestige relocks level-based pistol access and finishes while preserving mastery rewards and skin selections.
The equipment picker clears saved pistols that the current level no longer allows. Trusted
round-result responses refresh rank and cosmetic entitlements on the server before the following round.

Each pistol independently earns Night Ops at its unlock level +5 and Cryo at +10, using the current prestige's level.
Mastery rewards are Woodland/Desert/Digital at 50/150/300 kills, Red Tiger/Blue Tiger at 75/200 headshot kills, and
Gold after all five milestones. Existing primary Gold requirements remain unchanged. Damage records the source
weapon so changing guns after hitting a victim does not change mastery credit.

Cosmetic slots 6–9 retain the original four pistols; slot 10 is the unsuppressed variant. Existing IDs, bits and slots remain stable. The picker and Armoury show pistols in unlock order. `CosmeticLook`
retains the original 64-bit word and adds a second, producing up to 128-bit hexadecimal looks. Old looks still parse;
four PRI integers carry both words. Armoury, account profile choices, hub looks and previews retain individual finishes.

## Presentation and content

The first-person stance follows the requested CS2-style framing: both hands together on the grip, a forward-pointing
barrel in the lower right, restrained recoil, and a lowered draw transitioning into the ready stance. Authored
walk motion is reduced to one-quarter amplitude and runs at 45% of the inherited rifle walk rate. The .44 Magnum
has stronger upward camera recoil and a short visible muzzle rise. Pistol scenes and firing/reload streams are
prepared during map loading; primary models are also warmed before the first spawn. View models remain cached
across weapon swaps. Rifle and pistol reports play immediately on the firing client, with the server's owner copy
suppressed. Audio is drained after simulation and before model/effect rendering. Rifle view models are hidden while scoped.
Authored finger poses are fitted to each grip. Contextual gadget clips are retargeted onto the same rig; pistol melee
uses a dedicated compact pistol-whip arc, keeping the wrists and weapon together. The inactive weapon is carried on
the holster bone. Revolvers eject no casing on firing. Reload opens the cylinder, ejects six individually animated cases,
brings a loaded speedloader to the chambers, transfers fresh rounds, withdraws the empty loader and closes the cylinder.
The cylinder swings left. Its six original textured cartridges have individual joints: spent cases leave empty chambers,
and the same fresh rounds travel with the loader, seat in the chambers and remain when the loader withdraws.
Spent cases retain the source rim, primer and UVs, with the projectile removed. There are no fixed cartridges or
plain cylinder placeholders underneath them. Adding these joints preserves the source arms' inverse bind matrices.

`content/assets/sidearms/author.py` adapts four credited CC-BY 4.0 pistol assets into five variants, adds an original suppressor and
speedloader, and exports icons and editable Blender scenes. Original downloads and licenses remain in `sources/`,
and `CREDITS.txt` accompanies the shipped models. `svmbuild.pistol_arms` uses DJMaesen's credited, finger-rigged 2K
arms and authored pistol clips, retargets the converted merc contextual clips, and adds weapon mechanism and socket
tracks. Normal maps preserve glove, cloth and machined surface detail. Derived first-person animations stay in
the content output. The chars step generates 40 pistol finishes and matching sleeves for the existing merc finishes.
The audio step installs five credited single-shot reports and five reload tracks assembled from recorded handling sounds.
Firing assets use the rifle's 8 dB sound gain, bringing unsuppressed sidearms close to its playback level. The .44 uses
dcsimon's recorded .44 Magnum report (the credited IDontLikeFruit edit), with a single report isolated and no added dynamics processing.
Magazine release, insertion and slide closure occur at 14%, 56% and 66% of the reload; revolver cylinder opening,
ejection, loading and closure occur at 12%, 28%, 58% and 82%. Reload sound starts locally with the reload clip;
the accepted server reload relays it to other players without echoing it to the owner or sending a separate sound request.
Reaching zero ammo alone emits no reload sound. Cancelling a reload stops its sound.
Audio sources and licenses are listed in `content/assets/sidearms/sources/audio/sources.json` and `CREDITS.txt`;
USP variants use .45 ACP reference recordings; the recording profiles are credited without claiming every source is the exact displayed model.
The release content builder includes the authored export inputs.

`play-sidearms` performs a rendered host/client pass through every pistol, draw, fire, reload and return
to primary. It checks Cryo finishes on the magazine-fed pistols and captures eight reload stages of the .44's original
textured cartridges. Screenshots go under `SVM_PLAY_SHOTS`. This automated pass is separate from a player's manual assessment
of feel and animation quality.

`startup-weapons` hosts from Create Session, travels from the lobby to AquaE in a fresh process and checks the first
rifle, first pistol and immediate owner audio. The server retains the initial inventory request while its living pawn
waits for client acknowledgement, including cold loads exceeding the normal five-second combat RPC timeout. A living
unarmed pawn may finish a delayed initial kit grant without gaining extra weapons. Match launch has no default countdown.

## Session availability

Mercenary sidearms are opt-in. The **Sidearms** checkbox in Create Session and lobby Session Parameters defaults to off, independently of Alternate and Practice. The setting is saved with the setup (`SvmSidearms=0|1`), retained when changing maps/modes, and replicated in the round rules. With it off, the server rejects sidearm grants even for unrestricted players, and the equipment picker is disabled without discarding the saved pistol choice. Turning it on enables the existing level unlock rules. Local Play Level scenarios also default to off and can opt in through `PlaySettings.Sidearms`.
