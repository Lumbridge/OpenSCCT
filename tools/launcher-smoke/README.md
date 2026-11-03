# Launcher layout smoke

Run from the repository root on Windows:

```powershell
dotnet run --project tools/launcher-smoke
```

The harness creates fixture installs in the temporary folder and hosts launcher controls in non-activating, offscreen windows. It never calls the production startup hook, makes network requests, registers the Join protocol, starts a game, or reads the player's installation. Dialog HTTP uses a rejecting handler, and the run asserts that no requests were attempted. It checks primary actions, update deferral, status retention, cancellation, keyboard traversal and control bounds for fourteen states at default/minimum sizes and 100%, 150% and 200% scaling. It also checks the smaller client area at the launcher's outer minimum size and the Settings, Maps and Help dialog layouts at their minima.

PNG captures are written to `local/launcher-smoke/artifacts`. Pass a different output directory as the first argument after `--`. The harness alone uses DPI-unaware mode to obtain a deterministic 96-DPI baseline, then explicitly scales geometry and fonts for the larger captures. These are WinForms scaling simulations for visual regression review; moving the production launcher between monitors is still a manual PerMonitorV2 check.
