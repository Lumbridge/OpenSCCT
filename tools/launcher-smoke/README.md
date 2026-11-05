# Launcher layout smoke

Run from the repository root on Windows:

```powershell
dotnet run --project tools/launcher-smoke
```

The harness creates fixture installs in the temporary folder and hosts launcher controls in non-activating, offscreen windows. It makes no network requests, registers no Join protocol, starts no game or editor, and does not read the player's installation. Its startup check disables update checks; dialog HTTP uses a rejecting handler, and the run asserts that no requests were attempted. It checks primary actions, update deferral, per-product status, tab switching during operations, queued Join requests, cancellation, keyboard traversal and control bounds for 25 Game/Editor states at default/minimum sizes and 100%, 150% and 200% scaling. It also checks the smaller client area at the launcher's outer minimum size and both palettes of the Settings, Maps and Help dialogs. Closing during a cancellable task waits for cleanup; closing during editor apply waits without cancellation. Use `-- --lifecycle` for just the close/startup checks.

PNG captures are written to `local/launcher-smoke/artifacts`. Pass a different output directory as the first argument after `--`. The harness alone uses DPI-unaware mode to obtain a deterministic 96-DPI baseline, then explicitly scales geometry and fonts for the larger captures. These are WinForms scaling simulations for visual regression review; moving the production launcher between monitors is still a manual PerMonitorV2 check.

Use `-- --settings` for the product-specific Settings checks and 12 captures at default/minimum size and
100%, 150% and 200%. These verify independent startup and preview preferences, product folders, the absence of
the other product's removal action, disabled uninstall for absent or unmanaged editor fixtures, and an available
uninstall for an interrupted managed install. Saving
preferences is exercised, but no removal confirmation or uninstall is invoked.
