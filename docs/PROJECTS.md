# OpenSCCT project directory

Choose a project by the game you want to play or the file you want to change.
The two original editions include the campaign and multiplayer modes shipped
with each platform. The Godot and Unreal modernisations focus on Spies vs. Mercenaries.

## Originals

| Project | Working source | Starting point |
|---|---|---|
| PlayStation 2 | [PS2 executable research](../decomp/ps2/) | Use the [disc setup guide](editions/ps2.md) to identify the original disc, then select the executable for the mode you are investigating. The launcher can also boot a local ISO in PCSX2. |
| Original PC / Steam | [PC executable research](../decomp/pc/) | Identify the campaign or Versus binary for the installed build. Use a separate folder for an optional Versus mod. |

## Modernisations

| Project | Working source | Starting point |
|---|---|---|
| OpenSCCT Godot | [Godot client](../game/), [shared simulation](../src/), [content converter](../content/) | Build the original Steam Versus maps and assets from the player's installation. Godot adds modern rendering, controls, practice and replay tools. |
| OpenSCCT Unreal | [OpenSCCT-Unreal](https://github.com/OpenSCCT/OpenSCCT-Unreal) | Work in `Source/OpenSCCT` for the Unreal presentation and `Bridge` for its connection to the shared C# simulation. |

## Launcher, website and shared systems

| Area | Where to work |
|---|---|
| Launcher | [Common install and edition services](../src/OpenScct.Launcher.Core/), [Windows UI](../src/OpenScct.Launcher/), [Linux UI](../src/OpenScct.Launcher.Linux/) |
| Website | [Player-facing pages](../src/OpenScct.Master/Player/) and [web assets](../src/OpenScct.Master/Web/) run with the [master service](../src/OpenScct.Master/). |
| Game simulation and online services | [C# projects](../src/) implement the simulation and dedicated server. The [original OpenSCCT client](../game/) runs in Godot. See the [architecture guide](ARCHITECTURE.md). |
| Content delivery | Build retail assets from the player's Steam installation. [OpenSCCT-Content](https://github.com/OpenSCCT/OpenSCCT-Content) maintains the downloadable community map catalogue. |

## Make a mod

| Target | What the launcher accepts | Tools and examples |
|---|---|---|
| PS2 | A separate, already-patched Chaos Theory ISO alongside the player's base image | Identify the target region and revision before applying the mod author's patch to a copy of the disc. |
| Original PC | A separate installed game or Versus mod profile | [Enhanced SCCT Versus](https://github.com/Joshhhuaaa/EnhancedSCCTVersus) includes [Versus Reloaded](https://allypal.github.io/SCCT_Versus_Reloaded/features/). [Reloaded Editor](https://github.com/OpenSCCT/SCCT_Versus_Reloaded_Editor) and [Map Manager](https://github.com/OpenSCCT/SCCT-Map-Manager) work with original PC Versus maps. |
| OpenSCCT Godot | Optional downloadable maps and maps built with OpenSCCT Editor | Select **Maps** for additional packs. The [Steam map list](../shared/SteamVersusMaps.txt) identifies the packages used for the local base build. |
| OpenSCCT Unreal | Compiled custom SVMap folders | Build a map in [OpenSCCT Editor](https://github.com/OpenSCCT/OpenSCCT-Editor). Follow the [custom map setup](UNREAL-MAP-MODS.md) to give the map a unique ID and enable it on launch. |

Community PC map releases and working assets live in [SCCT-Maps](https://github.com/OpenSCCT/SCCT-Maps).
Each mod should identify its edition, required build, install format, and how to return to stock.

## Read or report progress

| Question | Evidence |
|---|---|
| Which original executable has been identified, and which bytes have been rebuilt? | [Matching workflow and build reports](../decomp/README.md). Record the region or distribution, executable hash, measured code size and byte comparison for that build. |
| Which Versus behaviours have been specified, ported or checked? | [Simulation inventory](progress/COVERAGE.md), [specifications](../spec/README.md) and [comparison scenarios](../tests/scenarios/README.md). |
| What changed in a playable build? | [OpenSCCT releases](https://github.com/OpenSCCT/OpenSCCT/releases), [Unreal releases](https://github.com/OpenSCCT/OpenSCCT-Unreal/releases) and [Editor releases](https://github.com/OpenSCCT/OpenSCCT-Editor/releases). |

For a first contribution, pick an executable build, a gameplay specification, or an
individual map. Describe what you measured, include the tool or command, and
record what a second person would need to reproduce the result. Start with the
[contribution guide](../CONTRIBUTING.md) and [existing research tools](../re/README.md).
