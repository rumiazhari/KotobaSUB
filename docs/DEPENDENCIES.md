# Dependencies and license decisions

## Selected for foundation
C# and stable .NET 10, WPF and Windows Forms NotifyIcon from the Windows desktop framework; Win32 interop for native window behavior. No Chromium, Python, Electron or third-party tray library. .NET/WPF repositories use MIT; redistributed runtime includes Microsoft's notices. Target `net10.0-windows10.0.19041.0`, x64 Windows 11. The SDK is pinned to 10.0.401 in global.json and installed under .tools/dotnet. Framework-dependent development output requires .NET 10 Desktop Runtime; portable publishing should include it.

KotobaSUB uses GPL-3.0 to allow planned adaptation of Flying Lyrics. Copy its complete license into LICENSE. MetadataMatching.cs now adapts upstream cleanup/matching concepts with an attributed source header and a path-level entry in THIRD_PARTY_NOTICES. Lyrics content rights are separate from the extension source license.

## Evaluated for later milestones; not yet package references
- [NAudio](https://github.com/naudio/NAudio): MIT, Windows loopback support. Current upstream has a v3 package split and changed WASAPI API. Inspect a stable release and use the focused WASAPI package if it satisfies resampling needs; do not assume v2 sample code matches v3.
- [Whisper.net](https://github.com/sandrohanea/whisper.net): MIT managed/native integration, CPU plus optional CUDA runtimes. Pin matching managed/native versions. Validate GPU driver compatibility and real native inference before distributing. Download a multilingual Whisper model separately; record source, SHA256, model license and size. English-only models are unsuitable.
- SMTC is now implemented through the Windows SDK projection; desktop metadata availability still varies by producer.

## Primary platform references
[WPF transparency](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency) and [extended styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) inform HWND implementation. Browser CSS cannot validate these behaviors. Keep library/data versions and notices with each later integration, including transitive native components.

## Structured lyrics integration
The implicit SDK targeting package resolves Microsoft.Windows.SDK.NET.Ref 10.0.19041.57 (verified in project.assets.json and KotobaSUB.deps.json). It supplies Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll. The package nuspec requires license acceptance and links the [Windows SDK license](https://aka.ms/WinSDKLicenseURL); do not incorrectly classify the SDK package as MIT. Binary packaging must preserve/check applicable redistribution terms. No extra NuGet HTTP/cache package is used: HttpClient, JSON and file storage are part of .NET.

Live endpoint checks passed for LRCLIB `/api/search` and NetEase `/api/cloudsearch/pc` plus `/api/song/lyric`. NetEase endpoints are not a stable published API contract; HTTP/shape failures are logged and do not crash the overlay. API availability and lyrics content rights are separate from source-code licenses. No API credentials are required by the implemented clients.
## Selected offline Japanese dependencies
LibNMeCab 0.10.2 and LibNMeCab.IpaDicBin 0.10.0 are pinned. Reviewed upstream revision: 3d4ddad1845e7522648b73c20135190e556fabd0. NMeCab offers GPL-2.0-or-later or LGPL-2.1-or-later; this application uses the GPL option. IPADIC has separate NAIST/ICOT terms. Full notices are in docs/licenses and copied into application output.

Microsoft.Data.Sqlite / Core 10.0.12 declare MIT. SQLitePCLRaw bundle/core/provider/lib.e_sqlite3 2.1.12 declare Apache-2.0 in installed nuspecs, copyright 2014-2024 SourceGear, LLC. Official license texts are retained in docs/licenses. Windows binary redistribution checks remain a packaging task.

Official English JMdict source: https://www.edrdg.org/pub/Nihongo/JMdict_e.gz, downloaded 2026-09-15. SHA256: 6DCAD1D536DD275629EA232730D7DE50E2CC5098BF8BDF6227531CDA06177CD9. Import produced 218,776 entries and a 103,739,392-byte index. JMdict is CC BY-SA 4.0, credited to EDRDG / Jim Breen. The derived index retains that license and restructures English senses and restricted forms. Setup/update is explicit; dictionary queries remain offline. Archive and index are ignored by Git.
