# Verification — 2026-09-15

## Environment and commands
The initially empty workspace now contains a .NET 10.0.401 solution. SDK installation is isolated to `.tools/dotnet`. Build with `./tools/verify.ps1 -Native`; this disables SDK telemetry for the verification process. The application itself has no telemetry or network client.

Release build passed with 0 warnings and 0 errors. All six executable core regressions passed: settings limits, atomic overwrite/roundtrip, corrupt JSON recovery, unsupported schema reporting, log rotation and preview surface preservation. Eleven native smoke assertions passed: required extended styles, foreground preservation on show, HTTRANSPARENT, MA_NOACTIVATE, transparent corner alpha, unlock/relock styles, focus preservation after hide/show, minimum geometry, resized geometry snapshot and transparent empty state.

Native captures are generated under `artifacts/native-smoke`: default.png, large-wrapped.png, japanese-only.png, small-window.png and resized.png. Results are in results.json. Artifacts are ignored by Git. Actual WPF glyph captures were visually inspected. Integrated browser automation could not launch because its Windows sandbox helper failed; a headless installed Microsoft Edge with an isolated `.tools/edge-preview` profile successfully rendered the PNGs on light and dark backgrounds. Its screenshot is browser-preview.png. This browser page is a QA fixture, not application UI or an embedded browser.

## Bugs found and corrected
Resizing originally did not recompute line wrapping. SizeChanged now rebuilds the current rendered line. Small geometry could clip large text; a down-only Viewbox and font-relative wrap width keep the line inside its bounds. Soft shadows were insufficient on white backgrounds; glyph geometry now has a black outline with white fill drawn afterward so small readings remain visible. Rebuilt and reran all focused checks after these changes, then re-inspected the browser screenshot.

## Limits and remaining acceptance
The smoke harness exercises the real HWND and WPF renderer, but programmatic hit-test/style assertions do not prove physical clicks pass through to every application. No claim is made that Spotify, Netflix, YouTube Music or a borderless game was tested. Actual tray mouse interaction, physical hotkeys, mixed-DPI monitors, sleep/wake, display removal and game foreground behavior remain manual acceptance work. Full monitor-layout profiles, configurable hotkey assignments, freeze mode and the full customization list are later milestones. Current settings provide layer toggles and text size; geometry can be dragged/resized and is stored locally.

The native foundation is implemented and has automated/rendered evidence. It is not the MVP: no music provider, dictionary/tokenizer or audio recognition is connected, and normal startup intentionally renders no sample text. No external service or captured audio was used. No remote Git repository was configured in the starting workspace, so push is not available.

## Structured lyrics integration — 2026-09-15
Release build passed with zero warnings/errors. Twenty core regressions passed, covering LRC timestamp variants/offsets/blank cues/expiry, Japanese normalization and recording rejection, local kana aliases, monotonic timing, cache/offset roundtrips, provider parsing, rate-limit fallback, a 2 MB response limit, cancellation, pause/seek and stale/disposed track responses. Fourteen native assertions passed, including live context/font/global-sync settings. Browser review of actual WPF captures found and verified a fix for settings help-text clipping; context lines were inspected on light and dark backgrounds.

Live `--media-smoke --network --netease` initialized SMTC and retrieved fixed-query timed lyrics from LRCLIB:14214012, then verified cache reuse. NetEase returned 30 candidates and successfully fetched accepted record NetEase:2034742057, preserving original/tlyric/romalrc fields. These service checks use a fixed YOASOBI/アイドル query. Local SMTC inspection also observed a real paused Chrome media session; its metadata was not used as the live lyrics query.

`--media-smoke --session-fixture` registered a generated muted WAV as a real Windows session, detected its Japanese fixture title and artist, verified pause+seek to six seconds and resume+seek to twelve seconds, then removed the fixture and observed the original Chrome session again. This establishes actual Windows event integration, not merely mocked events. It does not prove every browser/player's metadata quality.

`--app-smoke` ran the actual tray shell with isolated local storage and sample preview, with SMTC discovery disabled. It reported TrayVisible=True, OverlayLocked=True and Source: Sample preview, then exited normally. Automatic approval review rejected a proposed shell test that could send the user's current listening metadata to external lyric services. The replacement shell test does not read/send that metadata. A full current-track browser-to-lyrics-to-overlay test remains unverified and requires authorization if the same review restriction applies.

Latest local commands: `./tools/verify.ps1 -Native`; `dotnet run --project src/KotobaSUB.Windows -c Release --no-build -- --media-smoke --network --netease`; `... --media-smoke --session-fixture --output artifacts/smtc-fixture`; `... --app-smoke`. The local SDK path is `.tools/dotnet/dotnet.exe`. Evidence is under artifacts/native-smoke, artifacts/media-smoke, artifacts/smtc-fixture and artifacts/app-smoke. Generated fixtures are not shipped. Full user acceptance, kanji-based cross-script matching, annotation, ASR, provider-priority routing, installer and performance/game validation remain outstanding.

## Offline Japanese learning checkpoint — 2026-09-15
Release verification passed: zero warnings/errors, 27 core regressions, 14 native assertions and six learning-renderer assertions. Real IPADIC and the complete 218,776-entry JMdict index produce 私 / は / 明日 / 学校 / に / 行きます, with I / tomorrow / school / go, lexical verb 行く and surface reading いきます. Tests cover restricted forms/senses, English gloss filtering, failed/cancelled imports preserving the previous database, missing-dictionary readings, span preservation, cache timing and stale completion rejection.

`./tools/setup-data.ps1 -UseExisting` successfully reimported the official archive and rebuilt the solution. `./tools/verify.ps1 -Native` passed against the deployed full index. The isolated `--app-smoke` also exited successfully. Learning smoke's first annotation including lazy initialization measured 343.7 ms on the final run, versus 1137.3 ms on an earlier cold run; this is not an ASR or steady-state latency claim.

Actual WPF captures were reviewed in headless Edge: artifacts/learning-smoke/browser-preview.png shows furigana, glosses, optional romaji and kana-only text on light/dark backgrounds. Updated native settings were inspected with no clipped controls or attribution. The preview page is a local QA artifact. No current listening metadata or audio was sent for these checks.

Remaining: dictionary-backed cross-script metadata aliases, ASR/audio capture, source routing, packaging and full physical/player acceptance. This checkpoint implements annotation, not the completed MVP. No Git remote is configured.

## Local metadata alias integration — 2026-09-15
Release build passed with zero warnings/errors and 29 core tests. Two new tests exercise the real IPADIC reader: 夜に駆ける / Yoru ni Kakeru in both directions; particle pronunciation; unknown kanji; rejection of 橋 versus 箸, wrong artist/duration/live version and additional title words. An offline source fixture verifies native-first queries, the three-query bound, worker execution and cached cross-script result reuse without another search. Cache revalidation rejects changed duration.

The native verification command also completed successfully; artifacts/native-smoke/results.json records 14 passes and artifacts/learning-smoke/results.json records six passes. This change adds no UI controls. Actual regenerated WPF captures are reviewed through the existing local headless Edge preview. No active media metadata is sent by these tests. Real service availability remains covered by the earlier fixed-query checks, not by the offline source fixture. Alternative readings, macron conventions and unknown names remain possible false negatives.

## Local audio and ASR checkpoint — 2026-09-15
The Windows project builds with zero warnings/errors using NAudio.Wasapi 3.1.0 and Whisper.net 1.9.1. After a clean restore, output inspection found only `runtimes/win-x64` with the four CPU Whisper DLLs; arm64 and x86 package assets are excluded. `tools/setup-model.ps1 -UseExisting` verified SHA256 60ED5BC3DD14EEA856493D334349B405782DDCAF0028D4B5DF4088345FBA2EFE for the 147,951,465-byte multilingual base model.

The deterministic suite covers split-callback PCM conversion, malformed alignment, non-finite floats, silence/noise rejection, speech pre-roll and trailing closure, capped windows/overlap, dropped-block discontinuities, confidence/no-speech/music hallucination rejection, repeated-prefix and overlap deduplication, single-inference sequencing, missing-model status, cancellation, disposal and simulated device faults.

A real CPU smoke decoded the licensed 0.95-second Japanese fixture and transcribed `こんにちは!` with aggregate segment confidence 0.89. The latest full Release run logged 716 ms for Whisper processing and 1,011 ms for the complete model/transcription operation on this machine. A second phase played that same fixture through Windows MediaPlayer and captured 0.40 seconds from the actual default WASAPI output at 48 kHz stereo 32-bit float; peak was 0.0229 with zero queue drops. It then stopped and restarted the same capture source and repeated the result, proving queue/semaphore reset and clean terminal wake behavior. Each cycle reported one clean stop. Machine-readable Release evidence is `artifacts/audio-smoke-release/results.json` and is ignored by Git. Fresh native and learning captures were also rendered into local browser galleries with headless Chrome after the integrated browser controller failed to initialize; the browser exited successfully for both pages.

This verifies one local CPU model, one short Japanese fixture and this machine's current default output device. It does not establish universal recognition accuracy, GPU support, output-device replacement behavior, long-session memory use or the requested 1–3 second end-to-end display latency. That M4-only limitation is superseded by the automatic-routing checkpoint below.

## Automatic provider routing checkpoint — 2026-09-15
Three deterministic routing regressions pass: 750 ms fallback hysteresis with four-second transcript expiry and partial/final utterance accumulation; structured-timeline priority through blank LRC cues; and fault/suspension clearing with hysteresis reset. The audio suite adds a short-utterance inactivity-flush regression. Existing cancellation and disposal tests continue to pass. Source labels no longer claim that ASR is uninstalled.

The first real routing attempt started WASAPI but produced no line because the 0.012 activity threshold exceeded the fixed fixture's measured 0.0105 maximum 20 ms block RMS. The threshold is now 0.004; a 0.003 below-threshold noise regression remains rejected. A second defect appeared after the visible pass: disposing an async iterator with an outstanding read logged `NotSupportedException`. The session now awaits that cancellation-bound read before disposal. A clean rerun contains only model load, 1.36-second window inference in 710 ms at confidence 0.78, and model release—no pipeline fault.

`--routing-smoke` disables SMTC and all lyric-network access, starts the production `PlaybackController` with no structured source, and plays the fixed non-looping 0.95-second Japanese fixture through Windows. Real default-output WASAPI and CPU Whisper route `こんにちは` through `LearningRenderer` into the actual WPF overlay. The final harness then toggles preview on and off in the same dispatcher turn, verifies that the prior transcript clears, replays the fixture, and requires a fresh second result (`こんにちは!`). The first line appeared at 4,550 ms from harness start; playback began at 1,300 ms, yielding about 3,250 ms playback-to-overlay. The second fresh line appeared at 8,622 ms. Both 1.36-second inferences completed at confidence 0.78/0.88, and shutdown released the model without a pipeline fault. The transparent 1000×240 WPF capture contains visible glyph pixels; a headless Chrome gallery renders it over light and dark backgrounds at 1400×500. Evidence is ignored under `artifacts/routing-smoke`.

This proves automatic ASR fallback through the actual controller on this machine without external metadata disclosure. Structured priority and stale/fault races are deterministic policy tests; a live current-track switch from ASR to network lyrics was not run because it could disclose active media metadata. The full real-player/game matrix, device replacement, sleep/wake, long-session profiling, caption-provider slot, GPU option and installer remain M6 acceptance work.
## Explicit ASR model installation checkpoint — 2026-09-15
`WhisperModelInstaller` has deterministic HTTP-backed regressions for checksum-verified atomic replacement, valid local reuse without a second request, oversized/checksum-invalid response rejection with the prior model preserved, and mid-stream cancellation with `.download` cleanup. The implementation enforces the pinned base-model SHA256 and a 160,000,000-byte streamed limit.

The native `--app-smoke` used isolated storage and reported `ModelAction=Install local ASR model (141 MB)`, confirming that normal startup exposes an opt-in installer without downloading. The installer writes only to `%LOCALAPPDATA%/KotobaSUB/models` in normal operation, and successful installation calls the existing source retry path. A live 148 MB model download was not repeated because the same pinned file/checksum was already validated in the M4 setup and real inference tests.
## Freeze and Study checkpoint — 2026-09-15
The native smoke now verifies that Study mode enables token interaction only after click-through is removed and captures `study-mode.png`; the full suite remains green. `--freeze-smoke` uses the normal tray composition with sample content, disables SMTC startup, and records `TrayVisible=True`, `OverlayLocked=False`, `Frozen=True`, and the opt-in model action. The tray/hotkey path preserves the last subtitle, suspends ASR/session updates and restores the prior lock state on resume. Token inspection uses existing local annotation data; interactive physical clicking and long-form dictionary coverage remain manual acceptance work.
## Current-user startup checkpoint — 2026-09-15

`--startup-smoke` created a unique temporary HKCU Run value, read back `Enabled=True` and the exact command `"C:\Program Files\KotobaSUB\KotobaSUB.exe"`, removed it, and read back `Removed=True`. It does not create, alter or delete the real `KotobaSUB` Run value. The tray toggle itself is restricted to the published `KotobaSUB.exe`, so development `dotnet` launches cannot register a broken command. Physical logon behavior remains package/manual acceptance work.

## M6 monitor-aware placement checkpoint — 2026-09-15

Implemented persisted monitor device identity and normalized working-area placement. Missing saved monitors recover to a live primary display; existing coordinate-only settings continue to load and receive monitor metadata on their next save. The native smoke recorded active monitor placement and recovery from an unavailable monitor, and the regenerated Chrome preview was non-uniform with 55 sampled colors. Mixed-DPI, physical monitor removal and sleep/wake remain manual acceptance work.

## Windows package checkpoint — 2026-09-15

`tools/package.ps1 -Verify` published a framework-dependent win-x64 archive, expanded it into a clean directory and ran its actual `KotobaSUB.exe --app-smoke`; the result records `TrayVisible=True`. After restoring the required Microsoft win-x64 runtime packs, `tools/package.ps1 -SelfContained -Verify` produced a 132,532,713-byte self-contained ZIP and the extracted executable recorded the same successful tray result. Package assembly rejects Whisper models, developer audio fixtures and artifacts; the model remains an explicit post-install download. Installer UX, code signing and physical clean-machine installation remain unverified.

## M6 resource profile checkpoint — 2026-09-15

Added `tools/profile.ps1` to measure the actual Release executable in idle and fixed-fixture routing modes. Each report records sampled working set, private bytes and CPU milliseconds. Idle profiling completed successfully; the routing profile currently exits nonzero from the packaged process and needs a focused audio/model diagnosis before it can be used as acceptance evidence. No resource limit is claimed from the incomplete routing run.

The corrected `tools/profile.ps1 -Mode audio` run used absolute model/fixture paths and completed successfully with 15 samples: peak working set 433,377,280 bytes, peak private bytes 931,930,112 bytes and 9,375 ms process CPU. This is valid fixed-fixture inference/capture evidence; the automatic routing profile remains unaccepted while ambient output audio contaminates its windows.

## M6 settings customization checkpoint — 2026-09-15

The native settings window now exposes the existing token-spacing value as a live slider. Native smoke changed it to 16 and observed the updated `OverlaySettings`; the regenerated headless Chrome preview contained 58 sampled colors.

## M6 settings font checkpoint — 2026-09-15

Added a bounded editable font-family selector using the existing `OverlaySettings.FontFamily` value and live renderer path. Native smoke selected `Meiryo UI` and observed the changed settings value; the updated Chrome preview contained 58 sampled colors. Installed-font availability outside the listed defaults remains handled by the editable field.

## M6 settings opacity checkpoint — 2026-09-15

Added a validated Japanese-text opacity setting from 10% to 100%, wired into the actual token renderer and applied live. Native smoke set it to 60%; all regressions passed and the regenerated headless Chrome preview contained 54 sampled colors.

## M6 cache control checkpoint — 2026-09-15

Added a compact **Clear lyrics cache** tray action backed by `LyricsCache.Clear()`. It removes only cached lyric JSON files, preserves per-song sync offsets, and reports the count in a tray notification. The cache regression saved, cleared, and reloaded a candidate successfully; the complete native suite remains at 0 failures.
