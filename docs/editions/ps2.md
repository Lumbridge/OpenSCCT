# Play the PlayStation 2 edition

OpenSCCT starts the original *Splinter Cell Chaos Theory* disc in PCSX2. Use the game's own menus to choose a mode. PCSX2 manages your controller, memory cards, graphics settings and boot mode.

## Set up the original disc

1. Configure PCSX2 with a BIOS dumped from your console. Start PCSX2 once to complete its setup wizard. [PCSX2 setup](https://pcsx2.net/docs/setup/running/) · [BIOS guide](https://pcsx2.net/docs/setup/bios/)
2. Make an uncompressed `.iso` image of your PS2 DVD. If your backup is archived, extract the ISO first. [Disc dumping guide](https://pcsx2.net/docs/setup/discs/)
3. Open **Games** in the OpenSCCT launcher, choose **PlayStation 2**, then choose the disc image. Select your PCSX2 executable on Windows or the executable or executable AppImage on Linux.
4. Choose **Play**. The launcher passes the selected disc path to PCSX2 with your normal emulator settings.

The launcher opens the image read-only. It checks the ISO 9660 directory, the boot target in `SYSTEM.CNF` and the target's MIPS ELF header. These serials identify the supported retail releases. [PCSX2 game database](https://github.com/PCSX2/pcsx2/blob/master/bin/resources/GameIndex.yaml)

| Region | Disc serials |
| --- | --- |
| North America | `SLUS-21137` |
| Europe | `SLES-53007`, `SLES-53287` |
| Japan | `SLPM-66130`, `SLPM-66496` |

## Play with a disc mod

1. Follow the mod author's instructions for the region and revision of your disc. Use their specified patch tool and keep the resulting ISO in a separate file. If the author provides a checksum, verify it before applying the patch.
2. Open **Mods** for **PlayStation 2** and choose the patched ISO. It must have the same boot serial as your selected original.
3. Return to **Games** and choose **Play**. To switch back, choose **Use original disc** under **Mods**. You can select another patched copy whenever you want to change mods.

If you move a selected image, select its new path in the launcher. OpenSCCT stores file locations in `ps2.json` in the launcher's data directory.

## Reconstruction

The disc launcher runs the shipped executables. Contributors can inventory the disc's boot, offline and online programs and track byte comparisons for each. [PS2 reconstruction instructions](../../decomp/ps2/)
