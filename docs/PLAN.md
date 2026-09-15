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

## Milestone 2 implementation sequence (2026-09-15)
1. Add Core media snapshots, monotonic playback projection, LRC parser/timeline, and attributed metadata matching. Preserve raw names and reject version/duration mismatches before sync preference.
2. Add HTTP clients for LRCLIB and NetEase with bounded response/time limits; successful cache records retain raw original/translation/romanization. Failed searches are not cached permanently. Per-song offsets persist independently.
3. Add an event-driven SMTC adapter in Windows, marshaled onto the dispatcher, and a cancellable lyrics session controller. Track identity and async request generation must prevent old responses from replacing new media. No idle poll; schedule only the next cue boundary.
4. Wire automatic source status, retry/wrong match, sync correction and optional previous/next context into the existing tray/overlay. Preview remains explicit and suspends real rendering.
5. Add parser/scoring/timing/cache/cancellation regressions and real Windows SMTC/network smoke modes. Inspect actual native captures in the browser after UI changes. Record live-source limitations, build/test evidence, then commit.

## Status after structured-lyrics implementation
Milestone 2 now includes real SMTC event integration, LRCLIB/NetEase clients, strict candidate scoring, local kana/explicit aliases, synchronized LRC parsing, bounded successful cache, sync offsets, pause/retry, and optional context lines. Build, 20 core regressions, 14 native assertions, browser capture review, fixed-query live services, a real Windows generated-media event fixture and isolated tray-shell smoke passed. See VALIDATION.md for the exact evidence and remaining end-to-end restrictions. Do not redo these completed components.

Next: Milestone 3 Japanese tokenizer/readings/JMdict offline index, including dictionary-backed metadata reading aliases and learning annotation integration. Current lyrics carry no computed readings/glosses. Continue M4 local audio/ASR and M5 priority routing afterward. Full current-track browser playback acceptance, physical game interaction, mixed-DPI monitor reliability, persistent wrong-match exclusions and binary redistribution/packaging checks remain open. No remote was present; check before any later push.

## Milestone 3 implementation sequence (2026-09-15)
Use LibNMeCab 0.10.2 with LibNMeCab.IpaDicBin 0.10.0, reviewed against upstream revision 3d4ddad1845e7522648b73c20135190e556fabd0. The analyzer is GPL-2.0-or-later OR LGPL-2.1-or-later; select its GPL option within this GPL-3.0 application and retain upstream notices. IPADIC has its own NAIST notice. Use Microsoft.Data.Sqlite 10.0.12 for an offline JMdict index; keep data licensing separate (EDRDG Japanese/English dataset, CC BY-SA 4.0).

Add a Japanese integration project, Core token/dictionary contracts, and a reproducible data-import tool. Stream official JMdict_e XML with external resolution disabled; preserve entry IDs, spelling/reading restrictions, sense restrictions and English gloss ordering. Build into a temporary database and replace only after validation; store source hash/schema/date for cache invalidation. Do not check generated dictionary binaries into Git. Supply a repeatable update/setup script and source/license documentation.

Preserve input spans and whitespace in tokenizer output. Join contiguous auxiliary endings to verbs/adjectives (行き + ます), but do not guess readings for unknown kanji. Use lemma readings for inflected dictionary lookup, retain lexical readings for furigana, and suppress kana-only furigana. Annotate on a worker, cache bounded results, and reject stale completions after track changes. Default one concise gloss, no particle dictionary clutter, optional local romaji off by default. Validate real tokenizer + full imported dictionary, restriction fixtures, surface reconstruction, cancellation/cache, and real native/browser captures before commit.

## Offline learning checkpoint — 2026-09-15
Implemented and validated the M3 subtitle learning pipeline: real IPADIC analysis, restricted full JMdict SQLite lookup, reproducible import/update script, background annotation with stale-result rejection, bounded caches, kana-only reading suppression, optional romaji and retained dependency/data notices. Full dictionary example and native/browser rendering checks pass; see VALIDATION.md. Do not rebuild these components from scratch on continuation.

The separate M3 dictionary-backed metadata alias integration is still pending. Current lyric matching deliberately retains its prior native/explicit/kana rules. Next inspect MetadataMatching, LyricsResolver and LyricsCache, add conservative cross-script readings without accepting different-kanji homophones or weakening artist/duration/version gates, and run focused real-tokenizer matching tests. Then proceed to M4 audio/ASR and M5 routing. Full current-track disclosure authorization, physical game/mixed-DPI acceptance and binary packaging checks remain open. Generated archive/index and visual evidence remain ignored local files.

## Metadata reading alias implementation plan
Add an optional local reading callback to matching, resolver queries and cache revalidation. Generate readings lazily with real IPADIC on the resolver worker; unknown kanji must yield no alias. Use exact normalized reading equality only when one form contains kanji and the other is Latin. Never compare two different kanji spellings through their readings. Preserve title/artist/duration/version gates, three-query limits, native-first search and cancellation. Test real readings, unknown names, homophones, cache reuse, provider queries and existing native rendering.

## Metadata reading aliases completed — 2026-09-15
Implemented the planned conservative local alias integration in MetadataReadings, MetadataMatching, LyricsResolver, LyricsCache and PlaybackController. Real-tokenizer tests verify both script directions, particles, unknown kanji, different-kanji homophones, recording/artist/duration rejection, native-first bounded queries, off-caller-thread execution and cached alias revalidation. Release build and 29 core tests pass. M3 implementation is now complete within its documented accuracy limits; M4 audio/ASR is the next implementation milestone. Earlier checkpoint statements about pending metadata aliases are superseded by this entry.

## Milestone 4 implementation sequence (2026-09-15)
Pin stable NAudio.Wasapi 3.1.0 and Whisper.net / Whisper.net.Runtime 1.9.1 after recording their actual package and native-runtime licenses. CPU is the only enabled runtime for this milestone. Keep the multilingual ggml model outside Git and application packages; add an explicit checksum-verified setup command and never download a model during ordinary startup.

Add concrete Core audio/transcription contracts only with consumers. WASAPI loopback owns its capture device, converts supported shared-mode PCM/float input to normalized mono samples and resamples to 16 kHz without unbounded buffering. Capture callbacks feed a bounded channel; overload drops oldest audio and records the count. Device invalidation/end must surface as health state and support restart. Stop and dispose promptly without retaining captured audio on disk.

Add an activity gate with calibrated RMS thresholds, minimum voiced duration, trailing-silence closure and maximum-window closure. Rolling windows include a short prior-audio overlap but do not emit silence. A stable transcript assembler normalizes Whisper text, rejects empty/no-speech/low-confidence results, confirms repeated prefixes, suppresses overlapping duplicate text and resets on silence. Preserve segment timing honestly; do not invent word timing.

Load Whisper lazily from the configured local model, force language ja, disable translation and run one inference operation at a time off the dispatcher. Dispose the processor/factory after a measured idle interval or when ASR is stopped. Missing/incompatible model or runtime must report a tray status while leaving structured lyrics operational. Log model name, capture format, queue drops, inference latency and accepted confidence locally; never log or upload raw audio.

First validate conversion, activity/silence boundaries, rolling overlap, backlog, cancellation/disposal, device-loss transitions and transcript stability with deterministic samples. Then run a real WASAPI fixture through a generated Windows playback device and a real CPU Whisper inference with a licensed Japanese speech fixture and pinned model hash. Wire automatic source fallback only in M5 after M4 capture/transcription health is proven. Do not claim universal ASR from algorithm-only tests or from a missing model.

## Milestone 4 audio engine completed — 2026-09-15
Implemented and validated the M4 capture/transcription engine: bounded WASAPI loopback, stateful format conversion/resampling, activity gating, overlapping windows, stable transcript filtering, Japanese CPU Whisper inference, model checksum setup, idle disposal and explicit health/cancellation. Deterministic regressions and real local Japanese inference plus default-output capture pass. The Windows output contains only the pinned x64 CPU runtime. See VALIDATION.md for measured evidence and limits.

Next implement M5 routing in `PlaybackController`: synchronized lyrics remain primary while healthy/fresh; unavailable, rejected or expired lyrics may start ASR after bounded hysteresis; any eligible structured line must stop capture/inference and reject stale ASR completions by generation. Keep original Japanese rendering and existing annotation path. Do not present the application as MVP-complete until that routing, packaging and the physical player/game matrix pass.