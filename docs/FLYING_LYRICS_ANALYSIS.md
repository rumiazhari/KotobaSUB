# Flying Lyrics source review

Reviewed https://github.com/Crlyzd/flying-lyrics at `06b6d01e275f7db45331e6c6eba59ae480d4a7d3`. Complete GPLv3 license is in its LICENSE. Local reference clone is ignored under `.reference/flying-lyrics`.

`src/background/searchEngine.js` is the useful boundary: cleanTitle removes bracketed/trailing noise and extracts Japanese quoted titles; cleanArtist/extractPrimaryArtist normalize collaborations and CV decorations. Query metadata retains full/short/native/romanized alternatives. getBestAutoMatch deduplicates query passes and provider IDs, requests both sources, checks likely identities and resolves NetEase lyrics lazily in bounded batches. Keep that separation from rendering when adapting into C#.

scoreCandidate prioritizes synchronized lyrics, weighs title and artist similarity and subtracts duration difference beyond five seconds. Actual code uses a -12000 hard mismatch penalty (the nearby comment still says -8000), with relaxed cross-script conditions. KotobaSUB should reject incompatible duration/version candidates before ranking: title similarity alone and a large sync bonus are not sufficient evidence. Retain the original metadata in addition to cleaned aliases because live/remix/TV edits can differ materially in timing.

NetEase raw fetch keeps lyric/tlyric/romalrc separate; preserve this distinction. The reference romanization fallback calls Google endpoints; do not inherit that default network behavior. Supplied romanized metadata is useful, but local reading lookup is preferable for privacy. Arbitrary kanji names cannot be safely romanized by kana substitution.

`src/content/services.js`, parseLrcOrGeneratePseudoSync, recognizes decimal timestamps, parses a single timestamp per line and creates evenly spaced timing for plain lyrics. This parser cannot be reused unchanged: implement multiple tags, integer timestamps, offset metadata, sorting, blank-line clearing and bounded final-line duration. Do not use its pseudo-sync. Translation/romanization fallback is also coupled to browser state; only the separation of supplied vs computed layers is applicable.

`src/content/selectors.js` obtains browser DOM media state. It cannot serve as the native Windows metadata provider; replace it with SMTC. `src/background/storage.js` splits local and Chrome sync settings. Replace with local versioned storage and stable per-song keys; no cloud sync. Renderer, PiP drivers, popup UI, artwork, animation, onboarding and analytics are excluded entirely.

No upstream source has been copied into the Milestone 1 application. Later translated/adapted algorithms belong under an explicitly attributed `Adapted/FlyingLyrics` directory with upstream file and revision comments; update THIRD_PARTY_NOTICES and keep GPLv3 license accompanying distribution.
