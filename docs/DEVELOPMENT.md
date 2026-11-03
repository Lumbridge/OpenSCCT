# Build from source

[Documentation](README.md) · [Contributing](../CONTRIBUTING.md) · [Testing](TESTING.md)

The .NET simulation, server and tools build without original game assets. The Godot client
is a separate solution. Playing converted maps needs content; synthetic tests do not.

## Prerequisites

- Git.
- The .NET SDK selected by [global.json](../global.json).
- Python 3.10 or later for content tools and documentation checks. CI uses Python 3.10.
- PowerShell for the Windows helper scripts.
- The Godot **.NET** editor selected by [godot-version.txt](../godot-version.txt), if working on the client.

The [Linux guide](LINUX.md) covers its toolchain bootstrap, native export and Flatpak packaging.

## Build and test the .NET solution

From a terminal:

```powershell
git clone https://github.com/Lumbridge/OpenSCCT.git
cd OpenSCCT
dotnet build OpenSCCT.slnx
dotnet test --solution OpenSCCT.slnx --no-build --ignore-exit-code 8
```

This builds the simulation, server, launcher and tools. It does not build the Godot client.
The launcher UI targets Windows. Tests use Microsoft.Testing.Platform; exit code 8 is allowed
because a test project may have no tests yet.

## Build the Windows client

Run from the repository root:

```powershell
$godotExe = & ./tools/Get-Godot.ps1
dotnet build game/OpenScctGame.slnx
& $godotExe --editor --path game
```

The helper downloads and verifies the pinned Godot .NET editor under `tools/godot/`.
Let the editor import the project before running it.

For a headless check without original content:

```powershell
./tools/ci/godot-smoke.ps1 -Tests boot,testbox -ExpectedFailures selftest-fail
```

The script builds and imports the client, then checks both the success and failure paths.
See [testing](TESTING.md) to include the generated TestRange map.

## Prepare local content

Use your own Chaos Theory install for conversion work. The pipeline reads it without changing
it and writes ignored files under `.content/`.

```powershell
python -m pip install -r content/requirements.txt
$env:SVM_GAME_ROOT = 'C:\path\to\Splinter Cell Chaos Theory\Versus'
Push-Location content
python -m svmbuild --list
Pop-Location
```

Choose the steps you need in the [content pipeline guide](../content/README.md).
The [launcher guide](LAUNCHER.md) covers the player installation and download path.
Do not add converted content to Git; read the [provenance rules](../re/PROVENANCE.md).

## Find the code

| Folder | Purpose |
|---|---|
| `src/` | Simulation, networking, server, launcher and service libraries |
| `game/` | Godot client: input, views, audio, menus and rendering |
| `tests/` | .NET tests, synthetic content tests and comparison scenarios |
| `content/` | Python content conversion pipeline |
| `spec/` | Behavior specifications and supporting evidence |
| `re/` | Tools and metadata for studying the original game |
| `tools/` | CI, code generation, coverage, release and deployment helpers |
| `fixtures/` | Synthetic data that can run in CI |
| `local/`, `.content/` | Ignored local output and game-derived data |

Read [architecture](ARCHITECTURE.md) for dependencies and
[conventions](CONVENTIONS.md) before changing simulation code.
