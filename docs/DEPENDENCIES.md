# Dependencies and license decisions

## Selected for foundation
C# and stable .NET 10, WPF and Windows Forms NotifyIcon from the Windows desktop framework; Win32 interop for native window behavior. No Chromium, Python, Electron or third-party tray library. .NET/WPF repositories use MIT; redistributed runtime includes Microsoft's notices. Target `net10.0-windows10.0.19041.0`, x64 Windows 11. The SDK is pinned to 10.0.401 in global.json and installed under .tools/dotnet. Framework-dependent development output requires .NET 10 Desktop Runtime; portable publishing should include it.

KotobaSUB uses GPL-3.0 to allow planned adaptation of Flying Lyrics. Copy its complete license into LICENSE. MetadataMatching.cs now adapts upstream cleanup/matching concepts with an attributed source header and a path-level entry in THIRD_PARTY_NOTICES. Lyrics content rights are separate from the extension source license.

## Selected local audio and transcription dependencies
NAudio.Wasapi 3.1.0 is pinned for Windows shared-mode loopback capture; its license is MIT. The implementation uses the v3 `WasapiRecorderBuilder`, a bounded 32-block queue, explicit PCM/float conversion, mono downmix and stateful 16 kHz resampling. The activity gate defaults to 0.004 RMS, calibrated below the fixed loopback fixture's measured 0.0105 maximum 20 ms block RMS; a 0.003 noise regression remains rejected. Whisper.net and Whisper.net.Runtime 1.9.1 are pinned together under MIT. The Windows project excludes the package's general build targets and copies only the four win-x64 CPU DLLs, so arm64, x86, Metal and GPU runtimes are not distributed by the current build. Transitive Microsoft.Extensions.AI.Abstractions 10.2.0 and System.Numerics.Tensors 9.0.0 are MIT.

The local model is the multilingual whisper.cpp `ggml-base.bin`, downloaded from `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin`. Verified size: 147,951,465 bytes. SHA256: 60ED5BC3DD14EEA856493D334349B405782DDCAF0028D4B5DF4088345FBA2EFE. The model is ignored by Git and is not copied into application output. `tools/setup-model.ps1` and the explicit tray installer verify this checksum before atomic replacement. The tray installer streams at most 160,000,000 bytes into a same-directory temporary file, supports cancellation, and preserves a prior model when HTTP, size or hash validation fails. The original OpenAI Whisper model/code license is MIT; retained text is in `docs/licenses/OpenAI-Whisper-MIT.txt`.

The deterministic real-inference fixture is Wikimedia Commons `Wikibooksqsjapanese1-snd005.ogg`, transcoded to MP3 by Wikimedia and stored only in ignored test data. `tools/setup-audio-fixture.ps1` downloads the exact transcode and verifies it before replacement. It says こんにちは, is credited to uploader Nesnad, and is CC BY-SA 3.0. Local MP3 SHA256: 13DC9E5E8F7760410A67BFDB08A3F44B2DB379D6C94FD82D37BE282A755F886B; size 19,666 bytes. The license text is retained in `docs/licenses/CC-BY-SA-3.0.txt`.

SMTC is implemented through the Windows SDK projection; desktop metadata availability still varies by producer.

## Primary platform references
[WPF transparency](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency) and [extended styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) inform HWND implementation. Browser CSS cannot validate these behaviors. Keep library/data versions and notices with each later integration, including transitive native components.

## Structured lyrics integration
The implicit SDK targeting package resolves Microsoft.Windows.SDK.NET.Ref 10.0.19041.57 (verified in project.assets.json and KotobaSUB.deps.json). It supplies Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll. The package nuspec requires license acceptance and links the [Windows SDK license](https://aka.ms/WinSDKLicenseURL); do not incorrectly classify the SDK package as MIT. Binary packaging must preserve/check applicable redistribution terms. No extra NuGet HTTP/cache package is used: HttpClient, JSON and file storage are part of .NET.

Live endpoint checks passed for LRCLIB `/api/search` and NetEase `/api/cloudsearch/pc` plus `/api/song/lyric`. NetEase endpoints are not a stable published API contract; HTTP/shape failures are logged and do not crash the overlay. API availability and lyrics content rights are separate from source-code licenses. No API credentials are required by the implemented clients.
## Selected offline Japanese dependencies
LibNMeCab 0.10.2 and LibNMeCab.IpaDicBin 0.10.0 are pinned. Reviewed upstream revision: 3d4ddad1845e7522648b73c20135190e556fabd0. NMeCab offers GPL-2.0-or-later or LGPL-2.1-or-later; this application uses the GPL option. IPADIC has separate NAIST/ICOT terms. Full notices are in docs/licenses and copied into application output.

Microsoft.Data.Sqlite / Core 10.0.12 declare MIT. SQLitePCLRaw bundle/core/provider/lib.e_sqlite3 2.1.12 declare Apache-2.0 in installed nuspecs, copyright 2014-2024 SourceGear, LLC. Official license texts are retained in docs/licenses. Windows binary redistribution checks remain a packaging task.

Official English JMdict source: https://www.edrdg.org/pub/Nihongo/JMdict_e.gz, downloaded 2026-09-15. SHA256: 6DCAD1D536DD275629EA232730D7DE50E2CC5098BF8BDF6227531CDA06177CD9. Import produced 218,776 entries and a 103,739,392-byte index. JMdict is CC BY-SA 4.0, credited to EDRDG / Jim Breen. The derived index retains that license and restructures English senses and restricted forms. Setup/update is explicit; dictionary queries remain offline. Archive and index are ignored by Git.
