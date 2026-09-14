# KotobaSUB implementation plan

## Starting point and scope
The workspace was empty on 2026-09-15, without Git or an SDK. The machine has x64 .NET 8.0.15 desktop/runtime packs. Install an isolated stable .NET 10 SDK in `.tools/dotnet`; do not change the system SDK. The user requests sequential milestones, beginning with architecture and immediately implementing the native overlay. This is not yet the audio/lyrics MVP.

## Milestone 0: foundation
- Inspect Flying Lyrics at commit `06b6d01e275f7db45331e6c6eba59ae480d4a7d3` (done; ignored clone under `.reference`).
- Write architecture, dependency and reference analysis documents before implementation.
- Create a .NET solution with a platform-independent Core library, Windows WPF shell, and executable regression harness. No external runtime package is needed for the first overlay milestone.
- Add Windows CI, bounded local logging, local atomic settings, GPL-3.0 license and notices.
- Build and test before committing; push only if a remote exists.

## Milestone 1: native overlay
Implement transparent text-only WPF rendering, Win32 topmost/no-activation/click-through styles, tray lifetime, hide/show, edit/lock, resizing, live settings, persisted geometry and conservative global hotkeys. Use explicitly requested sample subtitles, never fake recognition. Normal startup must remain empty until a real provider exists. No ASR controls claiming to listen.

Validate core persistence and invalid settings recovery. A native smoke harness must create the actual HWND, check styles and hit behavior, check focus preservation, render the actual WPF content to PNG and exercise edit/lock and settings changes. A browser preview should display these actual captures for visual review; it is not evidence of Windows interaction. Check 100%/150% sizing, wrapping, hidden reading/gloss layers and no opaque background. Record limitations honestly, especially actual game foreground tests and physical mouse tests.

## Milestone 2: structured lyrics
Add SMTC subscriptions, immutable track identity and monotonic playback anchor; cancel old requests on track changes. Search LRCLIB then bounded NetEase fallback, keeping raw original/translation/romanized fields separate. Adapt identified reference logic in a clearly attributed folder. Add hard duration/identity rejection before synchronized preference. Parse multiple timestamps, integer seconds, offsets, blank lines and final-line expiry. Never synthesize timed lyrics from plain text. Tests: native punctuation, title/artist normalization, aliases, duration mismatch, stale network response, pause/seek/track transitions, LRC boundaries, cache roundtrip. Cache versioned by metadata/provider ID, and store per-song offset separately.

## Milestone 3: Japanese annotation
Evaluate NMeCab with an explicit dictionary schema and redistribution license before pinning. Use lemma/reading/POS, retain surface offsets, combine inflection only with verified rules. Stream JMdict into indexed SQLite offline; retain entry IDs, sense restrictions and attribution. No character-by-character invented readings. Test 学校, 明日 ambiguity, 行きます, particles, unknown names, kana-only tokens and reading-restricted glosses. Annotate off the UI thread and cache by tokenizer/dictionary version. Add optional romaji only after a tested local Hepburn conversion.

## Milestone 4: audio ASR
Pin reviewed NAudio WASAPI and Whisper.net packages. Capture output audio into a bounded queue; downmix/resample to 16 kHz mono. Use activity gating and short overlapping windows, silence reset and stable-prefix deduplication. Run inference off UI thread; force Japanese, reject low confidence/silence hallucinations. Dispose idle model and cancel capture promptly. CPU first; GPU optional after native-runtime/driver verification. Log latency and resource measurements rather than claiming 1–3 seconds without evidence. Test silence, repeated utterances, overlap, device changes, disposal, backlog and CPU model execution with real Japanese fixtures.

## Milestone 5: routing
Eligible timed captions > confident synchronized lyrics > ASR. No caption scraper in initial scope. Routing needs media/session generation checks, explicit health/freshness, bounded fallback and hysteresis. Stop inference while structured data is active. A stalled/unavailable provider must not keep stale captions visible. Test cancellation, failure, fallback and track races.

## Milestone 6: polish
Freeze/word inspection, monitor-layout profiles, optional startup registration, model download with checksum and cancellation, packaging and full manual matrix. Test Chrome YouTube Music, Spotify, spoken YouTube, Netflix audio, VLC, MP3/MP4 and a borderless Japanese game; cover pause/resume/seek/skip/minimize, monitor changes, sleep/wake and output device replacement. Do not label MVP complete until the user's 13-step acceptance sequence passes with real providers.

## Continuation and recovery
Inspect Git status and this document first. Preserve existing edits. Local tools and reference downloads are ignored, not application dependencies. Use `.tools/dotnet/dotnet.exe` if no system SDK is present. Record exact commands/results in `docs/VALIDATION.md`; update completed/pending work after each milestone. Commit only verified changes, and distinguish local commit from successful remote push. Do not redo verified work merely to consume remaining task time.

## Status after foundation implementation
Milestone 0 documents, solution, license, logging, core regressions and Windows CI are in place. Milestone 1 native rendering, tray controls, basic hotkeys, edit/lock, live layer/text-size settings and geometry persistence are implemented. Release build, six core regressions, eleven native assertions and browser visual review passed; see VALIDATION.md for the important manual acceptance limits. M2–M6 remain unimplemented. Next work should begin with SMTC and structured lyrics after physical overlay acceptance, not claim complete MVP.
