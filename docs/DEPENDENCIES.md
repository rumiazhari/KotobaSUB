# Dependencies and license decisions

## Selected for foundation
C# and stable .NET 10, WPF and Windows Forms NotifyIcon from the Windows desktop framework; Win32 interop for native window behavior. No Chromium, Python, Electron or third-party tray library. .NET/WPF repositories use MIT; redistributed runtime includes Microsoft's notices. Target `net10.0-windows`, x64 Windows 11. Install SDK locally using Microsoft's official installer; pin the actual SDK in global.json once installation completes. Framework-dependent development output requires .NET 10 Desktop Runtime; portable publishing should include it.

KotobaSUB uses GPL-3.0 to allow planned adaptation of Flying Lyrics. Copy its complete license into LICENSE. No Flying Lyrics source is shipped by the first overlay milestone; THIRD_PARTY_NOTICES identifies reviewed source and requires path-level attribution for subsequent adaptations. Lyrics content rights are separate from the extension source license.

## Evaluated for later milestones; not yet package references
- [NAudio](https://github.com/naudio/NAudio): MIT, Windows loopback support. Current upstream has a v3 package split and changed WASAPI API. Inspect a stable release and use the focused WASAPI package if it satisfies resampling needs; do not assume v2 sample code matches v3.
- [Whisper.net](https://github.com/sandrohanea/whisper.net): MIT managed/native integration, CPU plus optional CUDA runtimes. Pin matching managed/native versions. Validate GPU driver compatibility and real native inference before distributing. Download a multilingual Whisper model separately; record source, SHA256, model license and size. English-only models are unsuitable.
- [NMeCab](https://github.com/komutan/NMeCab): candidate managed morphological analyzer. Must check repository/license and NuGet release maintenance alongside IPADIC/UniDic format. Dictionary licensing is independent from analyzer licensing. Do not ship a guessed dictionary package or schema.
- [JMdict](https://www.edrdg.org/jmdict/j_jmdict.html): intended offline lexical dataset. Follow linked EDRDG licensing/attribution terms for the exact downloaded release and document redistribution before bundling. SQLite import retains readings, entry IDs and sense restrictions; do not flatten unrelated senses.
- Microsoft.Data.Sqlite: MIT candidate for dictionary/cache indexing. Not needed for Milestone 1's small settings file.
- SMTC: Windows API, event-driven global session manager. Add the appropriate Windows SDK target only at integration; desktop metadata availability varies by producer.

## Primary platform references
[WPF transparency](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency) and [extended styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) inform HWND implementation. Browser CSS cannot validate these behaviors. Keep library/data versions and notices with each later integration, including transitive native components.
