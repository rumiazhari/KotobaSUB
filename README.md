# KotobaSUB

A lightweight real-time Japanese learning subtitle overlay for Windows audio.

The intended application turns Japanese music/dialogue into furigana, Japanese text and concise word meanings above other Windows applications. No account, browser window or subtitle background panel. Speech recognition will be local; dictionary lookup will be offline.

**Current development state:** native overlay foundation only. Tray controls, transparent sample rendering, lock/unlock, resize, appearance settings and global hotkeys are implemented. Music detection, lyrics retrieval, Japanese analysis and audio transcription are not yet connected. This is not the MVP.

## Build and run

Requires Windows 11 x64 and .NET SDK 10.0.401. If installed locally, replace `dotnet` below with `.\.tools\dotnet\dotnet.exe`.

```powershell
dotnet build KotobaSUB.slnx -c Release
dotnet run --project tests/KotobaSUB.Tests -c Release
dotnet run --project src/KotobaSUB.Windows -c Release -- --preview
```

The tray's **Sample preview** explicitly displays test data. Without it, the overlay is empty. Ctrl+Alt+F9 hides/shows it; Ctrl+Alt+F10 unlocks/locks position. While unlocked, drag its edit area or bottom-right resize handle. Appearance settings apply immediately. Quit from the tray. Settings and bounded logs are in `%LOCALAPPDATA%/KotobaSUB`; no network requests or audio capture occur in this milestone.

```powershell
dotnet run --project src/KotobaSUB.Windows -c Release -- --smoke --output artifacts/native-smoke
```

The native smoke test checks HWND styles and foreground preservation and exports actual WPF PNGs. Run `./tools/verify.ps1 -Native` for the combined build/regression/native checks. See [validation evidence and limits](docs/VALIDATION.md). See [plan](docs/PLAN.md), [architecture](docs/ARCHITECTURE.md), [dependencies](docs/DEPENDENCIES.md), [reference analysis](docs/FLYING_LYRICS_ANALYSIS.md) and [notices](THIRD_PARTY_NOTICES.md). Licensed GPL-3.0.
