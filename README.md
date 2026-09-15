# KotobaSUB

A lightweight real-time Japanese learning subtitle overlay for Windows audio.

KotobaSUB displays text above other Windows applications, with no browser window, account or subtitle background panel. The intended learning layers are furigana, Japanese text and concise word meanings, with local speech recognition and an offline dictionary.

**Current development state:** the native overlay and structured-lyrics pipeline are implemented. Windows SMTC detects media metadata; LRCLIB and NetEase provide synchronized lyrics; playback events and timed boundaries drive the overlay. Offline IPADIC tokenization, JMdict meanings and optional romaji are connected. Audio transcription is not implemented yet. This is not the MVP.

## Build and run

Requires Windows 11 x64 and .NET SDK 10.0.401. If installed locally, replace `dotnet` below with `.\.tools\dotnet\dotnet.exe`.

```powershell
dotnet build KotobaSUB.slnx -c Release
dotnet run --project src/KotobaSUB.Windows -c Release
```

Normal mode automatically looks up the active Windows media session's song metadata. It may also look up a paused session. Only accepted synchronized lyrics are displayed; untimed or mismatched results leave the overlay empty while ASR is unavailable. Current matching supports native text, supplied alternate-script title aliases, and local kana romanization. Arbitrary kanji-to-romanized titles still need dictionary-backed readings.

Ctrl+Alt+F9 hides/shows the overlay; Ctrl+Alt+F10 unlocks/locks position. While unlocked, drag its edit area or bottom-right resize handle. The tray provides pause, wrong-lyrics/retry, per-song sync and settings. Positive sync offsets show lyrics earlier. Previous/next lines are optional. Settings changes apply immediately. Quit from the tray.

For sample data without lyric requests, add `-- --preview` to the run command. The tray's **Sample preview** also enables this explicitly. Sample readings/glosses are fixture data, not generated analysis.

## Privacy and storage

Normal lyric search sends song title, artist and derived search aliases to `lrclib.net` and, if needed, `music.163.com`. Candidate IDs are sent when fetching NetEase lyrics. Duration is used locally for matching; no captured audio is uploaded or recorded. HTTP requests include the application user agent and ordinary connection metadata. No telemetry, account, cloud translation or online romanization exists.

Settings, bounded logs, successful lyrics and per-song offsets live under `%LOCALAPPDATA%/KotobaSUB`. Lyrics cache records expire after 30 days and are limited to 128 files. Original lyrics, supplied translation and romanization remain separate.

## Verification

```powershell
./tools/verify.ps1 -Native
dotnet run --project src/KotobaSUB.Windows -c Release -- --media-smoke --session-fixture
dotnet run --project src/KotobaSUB.Windows -c Release -- --media-smoke --network --netease
dotnet run --project src/KotobaSUB.Windows -c Release -- --app-smoke
```

The first command builds, runs core regressions and checks the real native window/settings with PNG captures. The session fixture registers a generated, muted WAV with Windows and checks pause/resume/seek events, then disposes its session. The network smoke uses a fixed YOASOBI/アイドル test query; it does not send the currently playing track to lyric services. The app smoke uses isolated storage and sample data, disables media discovery, and exits automatically. Artifacts are written under `artifacts/` and are not committed.

See [validation evidence and limits](docs/VALIDATION.md), [plan](docs/PLAN.md), [architecture](docs/ARCHITECTURE.md), [dependencies](docs/DEPENDENCIES.md), [Flying Lyrics review](docs/FLYING_LYRICS_ANALYSIS.md) and [notices](THIRD_PARTY_NOTICES.md). Licensed GPL-3.0.

## Offline dictionary setup
Run `./tools/setup-data.ps1` before launching to download official English JMdict, build its SQLite index and copy it into application output. Repeat to update, then restart KotobaSUB. Use `./tools/setup-data.ps1 -UseExisting` to rebuild from the downloaded archive without network access. Import validates a temporary database before replacing the prior index. Generated data stays in ignored `.data/`.

IPADIC deploys through the pinned NuGet package. Without JMdict, readings work but meanings are unavailable. Analysis runs locally on a worker; original text appears while annotation completes. Romaji defaults off. A selected short gloss is a learning aid, not contextual translation. Annotation cache is bounded to 512 memory entries and 256 local files.

JMdict is © EDRDG / Jim Breen, CC BY-SA 4.0. The derived index retains that data license. See THIRD_PARTY_NOTICES.md and docs/licenses; local output copies notices into Data/. Full-data tests run automatically in tools/verify.ps1 when the index exists.
