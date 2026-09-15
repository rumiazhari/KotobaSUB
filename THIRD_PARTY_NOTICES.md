# Third-party notices

KotobaSUB is licensed under GNU GPL version 3; see LICENSE.

## Flying Lyrics

Flying Lyrics, https://github.com/Crlyzd/flying-lyrics, by Crlyzd and contributors, GPL-3.0. Reviewed revision: 06b6d01e275f7db45331e6c6eba59ae480d4a7d3.

`src/KotobaSUB.Core/Adapted/FlyingLyrics/MetadataMatching.cs` adapts cleanup rules and matching concepts from upstream `src/background/searchEngine.js`: Japanese quoted-title extraction, bracket/suffix noise removal, collaboration artist cleanup, alternate title forms and edit-distance comparison. The C# implementation adds Unicode compatibility normalization and strict recording-version/duration/identity gates; it does not port the original score constants or network romanization. Its source header preserves author/project/revision attribution. The full GPLv3 license is included in LICENSE. Other lyric client, timing, rendering, cache and kana-conversion code is newly implemented; upstream endpoint use is described in docs/FLYING_LYRICS_ANALYSIS.md.

## Microsoft platform components

.NET, WPF and Windows Forms are Microsoft/.NET Foundation components, principally MIT licensed. Framework-dependent builds use an installed runtime; self-contained distribution must retain runtime third-party notices.

The Windows target resolves `Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57, including Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll. Package copyright: © Microsoft Corporation. All rights reserved. The installed package specifies license acceptance and links to https://aka.ms/WinSDKLicenseURL. This SDK package is not labeled MIT here; retain/check its actual SDK license and redistributable terms when preparing a binary distribution. Current work delivers source and local development builds, not an installer.

No dictionary, ASR model, music file or lyrics dataset is bundled. Retrieved lyrics remain local cache data; provider content rights are distinct from application source licensing.
