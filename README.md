# KotobaSUB

A lightweight real-time Japanese learning subtitle overlay for Windows audio.

KotobaSUB displays text above other Windows applications, with no browser window, account or subtitle background panel. The intended learning layers are furigana, Japanese text and concise word meanings, with local speech recognition and an offline dictionary.

**Current development state:** the native overlay, structured-lyrics pipeline, offline Japanese annotation, and local audio-transcription engine are implemented. Windows SMTC detects media metadata; LRCLIB and NetEase provide synchronized lyrics; IPADIC and JMdict supply readings and concise meanings. The application automatically prefers accepted synchronized lyrics and otherwise starts local Japanese Whisper transcription after a short transition delay. Structured lyrics stop capture immediately, while ASR text follows the same IPADIC/JMdict annotation path. This is not yet called MVP-complete because the full physical player, game, monitor, sleep/wake, device-switching, packaging, and model-installation acceptance matrix remains open.

## Build and run

Requires Windows 11 x64 and .NET SDK 10.0.401. If installed locally, replace `dotnet` below with `.\.tools\dotnet\dotnet.exe`.

```powershell
dotnet build KotobaSUB.slnx -c Release
dotnet run --project src/KotobaSUB.Windows -c Release
```

Normal mode automatically looks up the active Windows media session's song metadata. It may also look up a paused session. Accepted synchronized lyrics take priority in normal mode. When no timed lyrics are available, KotobaSUB automatically captures the default Windows output and uses local Japanese ASR; preview and pause stop listening. Current matching supports native text, supplied aliases, local kana romanization and exact IPADIC-backed kanji-to-Latin readings. Unknown kanji are not guessed; alternative pronunciations and spelling conventions may still fail to match.

Ctrl+Alt+F9 hides/shows the overlay; Ctrl+Alt+F10 unlocks/locks position; Ctrl+Alt+F12 freezes or resumes the current subtitle for Study mode. While unlocked, drag its edit area or bottom-right resize handle. The tray provides pause, wrong-lyrics/retry, per-song sync, an opt-in **Start with Windows** toggle and settings. The settings window applies text size, token spacing, layer toggles and sync changes live. The startup toggle writes a quoted current-user Run entry only from the published `KotobaSUB.exe`; it never requires elevation. Positive sync offsets show lyrics earlier. Previous/next lines are optional. Settings changes apply immediately. Quit from the tray.

For sample data without lyric requests, add `-- --preview` to the run command. The tray's **Sample preview** also enables this explicitly. Sample readings/glosses are fixture data, not generated analysis.

## Local speech model setup

Use the tray item **Install local ASR model (141 MB)** to explicitly download and SHA256-verify the multilingual Whisper `base` model into `%LOCALAPPDATA%/KotobaSUB/models`. The item becomes Cancel while active; a failed or cancelled update preserves the prior model. Ordinary startup never downloads a model. Developers can instead run `./tools/setup-model.ps1` for ignored `.data/models/ggml-base.bin`; `-UseExisting` performs an offline checksum check. For the licensed real-inference QA fixture, run `./tools/setup-audio-fixture.ps1`; `-UseExisting` performs an offline checksum check. The CPU path currently requires x64 Windows 11 and a processor with the instruction support required by the bundled Whisper runtime.

The implemented engine captures the default Windows output, converts shared-mode PCM/float audio to 16 kHz mono, gates silence, uses bounded overlapping windows, and suppresses low-confidence or repeated transcript fragments. Captured samples remain in bounded memory and are never written to disk or uploaded. The source router waits 750 ms before ASR fallback, expires stale ASR text after four seconds, and rejects results from obsolete capture generations.
## Privacy and storage

Normal lyric search sends song title, artist and derived search aliases to `lrclib.net` and, if needed, `music.163.com`. Candidate IDs are sent when fetching NetEase lyrics. Duration is used locally for matching; no captured audio is uploaded or recorded. HTTP requests include the application user agent and ordinary connection metadata. No telemetry, account, cloud translation or online romanization exists.

Settings, bounded logs, successful lyrics and per-song offsets live under `%LOCALAPPDATA%/KotobaSUB`. Lyrics cache records expire after 30 days and are limited to 128 files. Original lyrics, supplied translation and romanization remain separate.

## Verification

```powershell
./tools/verify.ps1 -Native
dotnet run --project src/KotobaSUB.Windows -c Release -- --media-smoke --session-fixture
dotnet run --project src/KotobaSUB.Windows -c Release -- --media-smoke --network --netease
dotnet run --project src/KotobaSUB.Windows -c Release -- --app-smoke
./tools/setup-model.ps1 -UseExisting
./tools/setup-audio-fixture.ps1 -UseExisting
dotnet run --project src/KotobaSUB.Windows -c Release -- --audio-smoke --model .data/models/ggml-base.bin --fixture .data/fixtures/konnichiwa.mp3
```

The first command builds, runs core regressions and checks the real native window/settings with PNG captures. The session fixture registers a generated, muted WAV with Windows and checks pause/resume/seek events, then disposes its session. The network smoke uses a fixed YOASOBI/アイドル test query; it does not send the currently playing track to lyric services. The app smoke uses isolated storage and sample data, disables media discovery, and exits automatically. Artifacts are written under `artifacts/` and are not committed.

See [validation evidence and limits](docs/VALIDATION.md), [plan](docs/PLAN.md), [architecture](docs/ARCHITECTURE.md), [dependencies](docs/DEPENDENCIES.md), [Flying Lyrics review](docs/FLYING_LYRICS_ANALYSIS.md) and [notices](THIRD_PARTY_NOTICES.md). Licensed GPL-3.0.

## Offline dictionary setup
Run `./tools/setup-data.ps1` before launching to download official English JMdict, build its SQLite index and copy it into application output. Repeat to update, then restart KotobaSUB. Use `./tools/setup-data.ps1 -UseExisting` to rebuild from the downloaded archive without network access. Import validates a temporary database before replacing the prior index. Generated data stays in ignored `.data/`.

IPADIC deploys through the pinned NuGet package. Without JMdict, readings work but meanings are unavailable. Analysis runs locally on a worker; original text appears while annotation completes. Romaji defaults off. A selected short gloss is a learning aid, not contextual translation. Annotation cache is bounded to 512 memory entries and 256 local files.

JMdict is © EDRDG / Jim Breen, CC BY-SA 4.0. The derived index retains that data license. See THIRD_PARTY_NOTICES.md and docs/licenses; local output copies notices into Data/. Full-data tests run automatically in tools/verify.ps1 when the index exists.

## Windows packages

Run `./tools/package.ps1 -Verify` to publish a framework-dependent `win-x64` ZIP under `artifacts/package`, extract it into a clean folder, and run the packaged tray-shell smoke. The archive contains `KotobaSUB.exe`, its pinned runtime dependencies, IPADIC/JMdict data, licenses, `LICENSE`, `README.md`, and `THIRD_PARTY_NOTICES.md`. It excludes downloaded Whisper models, developer audio fixtures, and test artifacts.

Run `./tools/package.ps1 -SelfContained -Verify` for a standalone `win-x64` ZIP. A fresh checkout may first need `./.tools/dotnet/dotnet.exe restore src/KotobaSUB.Windows/KotobaSUB.Windows.csproj -r win-x64` to obtain Microsoft runtime packs. The model remains an explicit post-install tray download, so neither ZIP bundles it.

## Resource profiling

Run `./tools/profile.ps1 -Mode idle` for the tray's no-media baseline or `./tools/profile.ps1 -Mode audio` for the fixed local CPU inference and WASAPI fixture. `-Mode routing` profiles the complete automatic fallback path when the default output is quiet. Each run samples working set, private bytes and process CPU every 250 ms and writes a JSON report under `artifacts/profile`; no mode reads current listening metadata.
