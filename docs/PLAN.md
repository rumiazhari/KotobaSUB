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
Freeze/word inspection, monitor-layout profiles, optional startup registration, model download with checksum and cancellation, packaging and full manual matrix. Test Chrome YouTube Music, Spotify, spoken YouTube, Netflix audio, VLC, MP3/MP4 and a borderless Japanese game; cover pause/resume/seek/skip/minimize, monitor changes, sleep/wake and output device replacement. Do not label MVP complete until the 13-step acceptance sequence passes with real providers.

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

## Milestone 5 implementation sequence (2026-09-15)
Add a Core routing policy with explicit suspended, lyrics-searching, structured-timeline, ASR-health and transcript-freshness inputs. A structured timeline owns the overlay even during intentional blank cues. Without one, wait 750 ms before requesting ASR so short metadata/lyrics transitions do not churn the capture device. Expire an ASR line four seconds after receipt and clear it when capture becomes unavailable or faulted.

Compose the proven WASAPI and Whisper adapters in `PlaybackController`. Resolve the model from `%LOCALAPPDATA%/KotobaSUB/models/ggml-base.bin`, with the repository `.data/models` path as a development fallback. Serialize start/stop operations, increment a generation whenever ASR eligibility changes, marshal worker events to the WPF dispatcher, and reject every transcript or status callback from an obsolete generation. Stop capture promptly when lyrics become eligible, preview/pause begins, or shutdown starts.

Test routing priority, blank structured cues, hysteresis, transcript expiry, faults, recovery and stale generation rejection with deterministic time. Add an isolated application smoke that disables external media discovery, drives fake lyrics/ASR transitions through the same controller-facing coordinator, and records source/display changes. Then rerun real local inference, WASAPI restart, full native checks and browser capture review before committing.

## Milestone 5 automatic routing completed — 2026-09-15
Implemented and validated automatic structured-lyrics-to-local-ASR fallback in the production playback controller. The router preserves blank structured cues, delays capture churn, expires stale speech, accumulates stable utterance deltas and clears on faults/suspension. Capture transitions and dispatcher callbacks are generation-safe. A non-looping Japanese phrase passed twice from real Windows playback through WASAPI, CPU Whisper, Japanese annotation and the actual transparent overlay, with rapid preview suspension/resume between cycles, confirmed stale-line clearing and clean shutdown. No current media metadata was queried or transmitted during this validation.

M6 is next: implement freeze/word inspection without changing passive defaults, monitor/layout profiles, startup registration, an in-app checksum-verified model installation flow, cache controls/model selection, packaging, resource profiling and the physical Windows/player/game acceptance matrix. The MVP claim remains withheld until the original 13-step sequence is evidenced with real sources.
## Milestone 6 packaging and control sequence (2026-09-15)
First make local ASR installable without developer scripts. Add one checksum-pinned model installer shared by tray and tests. Stream into a same-directory temporary file with a strict size bound, incremental progress, cancellation and SHA256 verification; replace the active model only after full validation. A failed/cancelled update must preserve any existing valid model and remove its temporary file. The tray exposes one Install/Update action that becomes Cancel while active, never downloads during ordinary startup, and retries ASR after success.

Then add Freeze/Study as a secondary overlay state with a default global hotkey and tray action. Freeze must stop source updates/listening, retain the current annotated line, unlock interaction without activating on automatic changes, and restore the prior lock state on resume. Word inspection should expose the existing token surface, reading, lemma, part of speech and available local JMdict glosses without adding a dashboard or network request.

Add monitor-aware placement records and live appearance controls only where the existing renderer consumes them. Preserve legacy settings migration, clamp missing monitors safely, and test monitor removal/layout changes. Add opt-in current-user startup registration with exact executable quoting and a visible tray/settings toggle. No elevation or machine-wide registry key.

Finally publish a framework-dependent win-x64 package plus a self-contained installer/archive path, include license/data notices, omit developer fixtures and downloaded models, and verify a clean extracted launch. Measure idle, structured and ASR process resources on this machine. Run the available player/device matrix without disclosing current media metadata externally unless separately authorized; record every physical item that remains unverified rather than weakening the MVP definition.
## M6 explicit model installation completed — 2026-09-15
Implemented the first M6 packaging-control item: an opt-in tray installer for the pinned local Whisper base model, with incremental progress, cancel, size bound, SHA256 verification, temporary-file cleanup, atomic replacement and retry routing. Core failure/cancellation regression coverage and isolated native tray evidence pass. Continue with passive Freeze/Study, monitor-aware placement, current-user startup registration, packaging and physical acceptance work.
## M6 Freeze/Study checkpoint — 2026-09-15
Implemented the passive Freeze/Study control: tray action and Ctrl+Alt+F12 retain the existing annotated line, suspend source work, unlock only for study interaction, present local token facts/glosses, and restore the prior lock state on resume. Native capture and isolated shell smoke pass. Next M6 work is monitor-aware placement, current-user startup registration, package artifacts, profiling and physical acceptance.
## M6 current-user startup checkpoint — 2026-09-15

Implemented the opt-in tray startup toggle using the current-user Run registry key and exact executable quoting. The control only registers a published `KotobaSUB.exe`, needs no elevation, and removes the value when disabled. An isolated smoke test creates, validates and removes a unique temporary Run value. Next M6 work is monitor-aware placement, package artifacts, profiling and physical acceptance.

## M6 monitor-aware placement checkpoint — 2026-09-15

Implemented monitor-aware overlay persistence: save active device identity and normalized working-area coordinates, restore to that display when available, and recover to the primary display when it is missing. Existing settings migrate on the next save. Native smoke and rendered browser preview pass. Next M6 work is package artifacts, resource profiling and physical acceptance.

## M6 Windows package checkpoint — 2026-09-15

Implemented `tools/package.ps1` for framework-dependent and self-contained win-x64 ZIP artifacts. It assembles published output with the required data and notices, rejects developer models/fixtures/artifacts, writes a manifest, and optionally extracts then smoke-tests the actual executable. Both variants were verified from clean extractions. Next M6 work is resource profiling and physical acceptance; installer UX and signing are still required for a release-ready installer.

## M6 resource profiling checkpoint — 2026-09-15

Added a machine-readable profile harness for idle and fixed local-ASR routing. Idle sampling is complete; routing currently exposes a packaged-process exit failure that must be diagnosed before performance acceptance. Next work is routing-profile diagnosis and physical acceptance.

The profile harness resolves the pinned model and Japanese fixture to absolute paths before launching the Release executable, so measurements do not depend on the caller's working directory. The `audio` profile is the accepted local inference/capture measurement; `routing` remains a quiet-output diagnostic until the physical audio matrix is available.

## M6 settings customization checkpoint — 2026-09-15

Exposed the already-rendered token-spacing setting in the compact native settings window with live application and regression coverage. Continue with remaining appearance controls, cache controls and physical acceptance.

## M6 settings font checkpoint — 2026-09-15

Exposed the existing font-family setting in the compact native settings window with live updates and regression coverage. Continue with opacity/weight controls, cache controls and physical acceptance.

## M6 settings opacity checkpoint — 2026-09-15

Exposed Japanese text opacity in the compact settings window and connected it to rendering with live updates. Continue with remaining layer-specific appearance controls, cache controls and physical acceptance.

## M6 cache control checkpoint — 2026-09-15

Implemented the first cache-control surface: a tray action that clears lyric cache files while leaving sync offsets and settings intact. Regression coverage validates save, clear and cache miss behavior. Continue with model selection/remaining appearance controls and physical acceptance.

## M6 layer opacity checkpoint — 2026-09-15

Implemented independent persisted opacity controls for furigana, gloss, supplied translation and inactive context lines, all applied through the existing renderer. Continue with remaining cache/model controls and physical acceptance.

## M6 settings startup checkpoint — 2026-09-15

Added the opt-in startup toggle to the native settings window, sharing the existing current-user registration guard with the tray. Continue with remaining model-selection controls and physical acceptance.

## M6 font weight checkpoint — 2026-09-15

Implemented the Japanese font-weight setting across persisted preferences, native controls and glyph rendering. Continue with model-selection behavior and physical acceptance.

## M6 text alignment checkpoint — 2026-09-15

Added a persisted Left/Center/Right text-alignment setting for the Japanese token row. The native settings selector applies the value immediately, validation clamps unknown values back to Center, and the renderer uses the selected alignment when laying out token stacks. Native smoke verified a live Left change; the regenerated headless Chrome preview produced 78 sampled colors. Physical readability across window widths and mixed-DPI monitors remains part of the open acceptance matrix.

## M6 line spacing checkpoint — 2026-09-15

Exposed persisted vertical line spacing (0–32 px) in the native settings window and applied it to the previous/current/translation/next line stack without adding a background panel. Native smoke set the value to 12 and observed the live preference; the regenerated headless Chrome preview produced 79 sampled colors. Mixed-DPI and physical readability checks remain open.

## M6 text contrast checkpoint — 2026-09-15

The glyph renderer now uses a persisted configurable black outline from 0 to 8 px, defaulting to 3 px, with geometry padding that scales with the outline so text cannot clip. The native settings window applies the outline live, and zero width preserves outline-free text when desired. Native smoke changed the value to 4.5 px; the capture contained 4,031 near-white glyph pixels and 6,010 near-black outline pixels. The headless Chrome gallery rendered successfully with 90 sampled colors. The overlay remains transparent and text-only; no subtitle background panel was added.

## Lyric verification and adaptive alignment plan — 2026-09-15

The current resolver is metadata-first and returns one candidate before playback audio can evaluate alternatives. The next lyric milestone is a bounded verification layer: retain 3–6 metadata-accepted candidates across LRCLIB/NetEase, compare partial local-ASR fragments against nearby lyric lines, require repeated separated evidence for verification/rejection, estimate robust automatic offset and conservative drift, and persist compact candidate/alignment profiles without raw audio. Manual `SongOffsets` remains independent and is applied after learned alignment. Implementation starts with deterministic Core comparison/alignment types, then bounded resolver retention, verifier integration, persistent learning, continuous low-duty probes and full smoke validation. Detailed design is in `docs/LYRIC_VERIFICATION.md`.