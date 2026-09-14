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
