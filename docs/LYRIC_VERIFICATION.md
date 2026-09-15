# Lyric verification and alignment plan

## Current baseline

`LyricsResolver` currently searches up to three bounded query forms per source, applies the existing `MetadataMatching.Evaluate` hard identity, duration and recording-version gates, fetches at most four accepted candidates in score order, and returns the first usable candidate. That is safe for recall and latency, but it discards the remaining plausible candidates before playback audio can provide evidence. `LyricsCache` stores one `LyricsCandidate` per track identity and revalidates metadata on load.

`LyricsSession` owns one selected candidate and one immutable `LyricTimeline`. Its persisted learning profile records verified and rejected candidates across restarts. `SongOffsets` stores only the user’s manual per-track correction. `LrcParser` and `LyricTimeline` preserve provider timestamps; no automatic alignment layer exists.

`AudioTranscriptionSession` already exposes bounded, local `TranscriptionSegment` events with segment start/end times, confidence and final-window state. `PlaybackController` generation-checks ASR callbacks and currently stops ASR whenever a structured timeline exists because ASR is only a fallback display source. Verification will add a separate, low-duty evidence path without replacing provider text or weakening the fallback path.

## Invariants

- Existing metadata hard gates remain the first filter. A duration/version/title/artist rejection cannot be rescued by an audio match.
- Provider lyric text and timestamps remain immutable source data. Displayed time is derived as provider timestamp, learned automatic alignment, then manual offset.
- ASR is evidence only. The selected provider candidate remains the displayed Japanese text.
- Audio, microphone data and raw ASR windows are never persisted.
- Candidate queries, fetched candidates, retained evidence and persisted profiles are bounded.
- Every asynchronous result carries track identity, candidate key and a monotonically increasing generation. Stale work can only be discarded.
- Normal playback remains passive. Verification cannot seek or pause the user’s media.
- A weak or missing ASR fragment is inconclusive. Rejection requires repeated, high-confidence disagreement.

## Domain model

Add a Core-only verification model in `KotobaSUB.Core.Lyrics`:

- `LyricCandidateEvidence` records candidate key, metadata score, matched lyric index, provider time, observed audio time, text similarity, ASR confidence, residual and timestamp.
- `LyricComparison` reports normalized character similarity, ordered-subsequence ratio, token overlap and a conservative combined score. Kanji-to-reading matches are supplemental and do not make different kanji spellings equal by themselves.
- `LyricConfidenceState` is `Unknown`, `Tentative`, `Probable`, `Verified` or `Rejected`.
- `LyricCandidateAssessment` contains state, metadata score, audio score, independent-anchor count, coverage and rejection reason.
- `LyricAlignmentModel` contains optional constant offset, optional bounded drift, anchor count, residual and a monotonic projection method. It stores automatic values separately from the manual `SongOffsets` value.
- `LyricVerificationProfile` is the versioned persisted record keyed by track identity and candidate key. It contains state, scores, verification/rejection counts, alignment model, compact anchors and `LastVerified`.

The profile schema is versioned and capped per track and globally. A malformed, oversized or future-version file is ignored with a local log message, leaving normal lyric lookup available.

## Candidate retention

Introduce a bounded candidate-result API while preserving the existing `ILyricsProvider.ResolveAsync` compatibility wrapper. The new resolver path gathers candidates from LRCLIB and NetEase across the existing native, alias and local-reading query forms, deduplicates by provider key, applies `MetadataMatching.Evaluate`, and retains the strongest 3–6 accepted candidates overall. Search responses remain capped at 30 per query and query count remains capped at three per source.

Each retained candidate is fetched only once, only when it has synchronized text, and only in bounded metadata-score order. Provider failures do not abort other candidates. The shortlist is returned with source, metadata decision and fetched text; the old single-result method selects the first candidate for callers that have not opted into verification.

Persistent rejected keys are removed before ranking. A manual retry can explicitly clear one profile or all profiles for a track; rejection is never irreversible.

## Deterministic Japanese comparison

Normalize candidate and ASR fragments with Unicode FormKC, whitespace and punctuation removal, and a consistent hiragana/katakana representation for the comparison copy. Preserve the original strings for diagnostics. Compare several nearby lyric lines rather than only the nominal current line.

Use multiple signals: normalized character edit similarity, ordered subsequence coverage for partial singing recognition, token overlap where an analyzer is available, and optional exact reading support for kana/kanji variants. Require a minimum amount of meaningful Japanese text before positive evidence. Homophones with unrelated kanji remain weak unless other signals agree.

The combined score is documented and tested as a conservative function of the signals. A partial fragment such as `君の知らない` can support `君の知らない物語`; an unrelated fragment such as `愛してる` cannot support an unrelated candidate merely because both are Japanese.

## Confidence and state transitions

Metadata score creates `Tentative` eligibility only after all existing hard gates pass. One positive anchor can raise a candidate to `Probable`; at least three separated high-confidence anchors with coherent timing and sufficient coverage are required for `Verified`. One weak or low-confidence ASR result never rejects a candidate.

Repeated strong disagreement lowers the audio score. `Rejected` requires at least two separated contradictory probes, each above the ASR confidence and text-evidence thresholds, or an explicit manual Wrong Lyrics action. State transitions are monotonic within a verification generation except for `Probable`/`Verified` decay after an extended period without supporting evidence. Candidate switching uses hysteresis: the replacement must be verified or materially exceed the current assessment for multiple probes before display changes.

Thresholds are constants with named tests, not scattered literals. Diagnostics record the candidate key, metadata score, evidence score, anchor count, residual, state transition and rejection reason without raw audio.

## Audio probes and lifecycle

Add a `LyricVerifier` that accepts a track identity, candidate timeline and timestamped ASR fragments and returns evidence plus an updated assessment. It has no WPF dependency and never changes displayed text.

During normal structured playback, verification runs in strategic, low-duty windows around expected lyric transitions and only while the candidate is `Tentative` or `Probable`, then backs off after `Verified`. The existing ASR fallback remains unchanged when no structured timeline exists. Probe scheduling must not seek or pause media; it maps capture-relative segment time to the media snapshot captured when a probe starts.

The playback integration owns a verification generation alongside the existing ASR generation. Song changes, retries, pauses, freezes, shutdown, candidate switches and cancellation invalidate all pending probe, comparison, alignment and persistence continuations. A callback from an old track or candidate is ignored before it can alter the overlay, cache or profile.

## Robust alignment

For each accepted evidence anchor, calculate `audioTime - providerTime` in seconds. Positive automatic offset means audio is later than the provider cue, so the displayed lookup position advances by that positive amount; this sign is tested explicitly and remains separate from the user’s manual offset.

Estimate a constant offset with a confidence-weighted median or trimmed median. Reject anchors with large residuals before recomputing. Once enough separated anchors exist, estimate `audioTime = a * providerTime + b` with a conservative drift bound around `a = 1`; use a robust slope estimator before fitting. Reject absurd drift and any model that would make the corrected timeline non-monotonic.

The projection clamps to bounded offset/drift limits and preserves cue order. If a future implementation needs piecewise correction, it must use a monotonic map and only activate after enough trustworthy anchors; this first implementation should prefer the bounded linear model.

## Persistence and manual actions

Implement `LyricVerificationStore` as a bounded versioned JSON store under the existing KotobaSUB local-data directory. The key is `(TrackIdentity, CandidateKey)`. Save compact evidence and learned alignment only after accepted state changes, using a temporary same-directory file and atomic replace. Keep manual `SongOffsets` untouched and compose it after automatic alignment.

On a later playback, load a verified profile after rechecking current metadata and candidate identity. Use its candidate as an immediate preferred result, then perform quiet background revalidation. A persisted rejection suppresses that candidate from automatic selection but can be cleared by the existing Wrong Lyrics/Retry path. Store no raw audio and no full ASR transcripts.

## Implementation stages

1. Add comparison, evidence, confidence and alignment domain types with deterministic tests for partial matches, disagreement, robust median, sign, drift bounds and monotonic projection.
2. Add bounded multi-candidate retention to the resolver and a compatibility single-result path. Extend cache representation without invalidating existing lyric cache files.
3. Add `LyricVerifier` and isolated tests for separated probes, weak evidence, repeated contradiction, hysteresis and generation identity.
4. Integrate candidate assessments and strategic probes into `LyricsSession`/`PlaybackController` without changing structured-text rendering or ASR fallback behavior.
5. Add `LyricVerificationStore`, manual rejection/clear operations, safe migration and restart tests; keep `SongOffsets` independent.
6. Add quiet continuous revalidation, diagnostics and resource measurements. Re-run native, media, audio, routing and package smokes.
7. Update architecture, validation and README documentation with tested claims and explicit singing-ASR limitations. Only then perform controlled public/fixed-source validation; do not send current private media metadata without authorization.

## Current status

Stages 1-4 are implemented in the Core and Windows playback path. The resolver retains up to six metadata-accepted synchronized candidates, `LyricVerifier` consumes local ASR fragments as evidence, and `LyricsSession` applies robust offset/drift models without mutating provider timestamps. `LyricVerificationStore` persists bounded verified/rejected profiles and compact anchors in versioned JSON; it stores no audio or transcript history. Structured playback keeps selected provider text authoritative while local ASR runs only while the selected candidate remains unresolved. Transcript callbacks carry media and ASR generations so stale results cannot affect a later track. The tray exposes a reversible clear-learned-decisions action. Strategic window scheduling, richer piecewise drift and controlled real-audio acceptance remain follow-up work.
## Timing and acquisition hardening — 2026-09-15

`AudioMediaClock` maps monotonic capture sample positions to the current SMTC media position. `AudioTranscriptionSession` emits capture observations and discontinuities; `PlaybackController` uses the center of each segment's capture interval for evidence and logs inference latency separately. Identity changes, seek jumps, pause/resume, rate changes, capture restart and dropped/discontinuous blocks force re-anchoring. Wall-clock completion time is never used as an anchor.

Verification uses four-second probe windows with adaptive six-to-sixty-second backoff. Verified candidates stop probing. Unverified cache entries are shown immediately while provider discovery continues; verified learning profiles may return immediately without an aggressive search. `LyricVerifier` uses a bounded ±24 second acquisition window until alignment has three anchors, then a ±4 second tracking window with distance and monotonic line penalties.

## Capture-time clock and low-duty probe hardening — 2026-09-16

`AudioBlock` carries the monotonic `CapturedAt` timestamp taken when a WASAPI buffer is received and a `CaptureGeneration` incremented for each loopback start. `AudioTranscriptionSession` forwards that timestamp unchanged; queue delay and Whisper completion time cannot move lyric evidence. `AudioMediaClock` treats `SpeechWindow`/`TranscriptionSegment` times as absolute 16 kHz capture-session sample positions. Re-anchoring stores the observed absolute sample and media position, so mapping uses `anchorMedia + (sample - anchorSample) * rate`; an absolute sample is never added to the anchor twice. A generation change, media identity change, seek, pause/rate transition or discontinuity invalidates the prior correlation.

Evidence uses the center of each transcription segment for lyric matching. ASR completion latency is logged separately for diagnostics and is never part of the residual or learned offset. Positive automatic offset means captured audio occurs later than the provider cue; manual `SongOffsets` remains a separate correction applied after the learned model.

Structured verification uses four-second low-duty probes. An intentional probe stop requests `AudioTranscriptionSession.StopAsync(flushPending: true)`, which flushes a final gate window only when it contains sufficient voiced samples; ordinary shutdown, song-change cancellation and faults do not flush. The scheduler timer includes the active probe end and backs off from six seconds to sixty seconds based on measured inference latency; verified candidates disable further probes. The no-structured-lyrics route still uses the existing continuous fallback ASR decision.

The deterministic suite now proves that continuous voiced four-second probes reach the transcriber, inference delay does not alter mapped media time, non-zero absolute sample anchors are not double-counted, repeated zero-based capture sessions re-anchor by generation, and pause/seek/rate/discontinuity transitions remain safe. These are deterministic timing guarantees; real-song singing recognition and end-to-end visual sync still require controlled public-media acceptance.

## Missing lyric recovery — 2026-09-16
Local diagnostics showed accepted provider candidates followed by a WPF cross-thread exception. LyricsSession now resumes provider awaits on its caller synchronization context. PlaybackController allows local ASR display without an SMTC snapshot or capture/media mapping; only lyric verification requires mapped media time. Provider discovery can inspect up to twelve fetched candidates to retain six timed results, so six leading untimed results no longer hide a later usable candidate. Fallback mode no longer schedules the unused lyric probe timer every 15 ms.

Release build and full native verification passed (zero failures). New deterministic tests cover UI-context completion and the seventh candidate after six untimed results. The actual WPF routing harness with --simulate-transcript displayed two controlled Japanese transcripts across suspension without metadata; its browser capture was visually checked. The real loopback speech smoke timed out with continuous six-second windows, so live singing recognition is not claimed. Whisper Base recognition, missing provider entries and mismatched browser metadata remain limitations; acceptance thresholds were not weakened.
