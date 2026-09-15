# Third-party notices

KotobaSUB is licensed under GNU GPL version 3; see LICENSE.

## Flying Lyrics

Flying Lyrics, https://github.com/Crlyzd/flying-lyrics, by Crlyzd and contributors, GPL-3.0. Reviewed revision: 06b6d01e275f7db45331e6c6eba59ae480d4a7d3.

`src/KotobaSUB.Core/Adapted/FlyingLyrics/MetadataMatching.cs` adapts cleanup rules and matching concepts from upstream `src/background/searchEngine.js`: Japanese quoted-title extraction, bracket/suffix noise removal, collaboration artist cleanup, alternate title forms and edit-distance comparison. The C# implementation adds Unicode compatibility normalization and strict recording-version/duration/identity gates; it does not port the original score constants or network romanization. Its source header preserves author/project/revision attribution. The full GPLv3 license is included in LICENSE. Other lyric client, timing, rendering, cache and kana-conversion code is newly implemented; upstream endpoint use is described in docs/FLYING_LYRICS_ANALYSIS.md.

## Microsoft platform components

.NET, WPF and Windows Forms are Microsoft/.NET Foundation components, principally MIT licensed. Framework-dependent builds use an installed runtime; self-contained distribution must retain runtime third-party notices.

The Windows target resolves `Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57, including Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll. Package copyright: © Microsoft Corporation. All rights reserved. The installed package specifies license acceptance and links to https://aka.ms/WinSDKLicenseURL. This SDK package is not labeled MIT here; retain/check its actual SDK license and redistributable terms when preparing a binary distribution. Current work delivers source and local development builds, not an installer.

Development builds deploy IPADIC and, after explicit setup, a derived JMdict index. No ASR model, music file or lyrics dataset is bundled. Retrieved lyrics remain local cache data; provider content rights are distinct from application source licensing.

## NMeCab and IPADIC
LibNMeCab 0.10.2, by Tsuyoshi Komuta, derives from MeCab by Taku Kudo / NTT and MeCab.DotNet by Kouji Matsui. Source: https://github.com/komutan/NMeCab, reviewed revision 3d4ddad1845e7522648b73c20135190e556fabd0. GPL-2.0-or-later OR LGPL-2.1-or-later; the GPL option is used here. Full notices: docs/licenses/NMeCab-COPYING.txt, NMeCab-GPL.txt and NMeCab-LGPL.txt. LibNMeCab.IpaDicBin 0.10.0 has separate NAIST/ICOT notices in docs/licenses/IPADIC-COPYING.txt and deployed IpaDic/COPYING.

## JMdict
JMdict is copyright Electronic Dictionary Research and Development Group (EDRDG) / Jim Breen, under Creative Commons Attribution-ShareAlike 4.0. Source: https://www.edrdg.org/jmdict/j_jmdict.html. Terms: https://www.edrdg.org/edrdg/licence.html. License: https://creativecommons.org/licenses/by-sa/4.0/.

KotobaSUB's derived SQLite index restructures English senses, forms and restrictions for lookup, retaining CC BY-SA 4.0 for the data separately from application code. Terms are retained in docs/licenses/JMdict-EDRDG.html and CC-BY-SA-4.0.txt. The importer records source hash/date. Run tools/setup-data.ps1 to obtain/update the official archive and rebuild. The tested source hash is documented in docs/DEPENDENCIES.md.

## SQLite integration
Microsoft.Data.Sqlite / Core 10.0.12: © Microsoft Corporation, MIT; see docs/licenses/Microsoft.Data.Sqlite-MIT.txt and https://github.com/dotnet/efcore.

SQLitePCLRaw bundle/core/provider/lib.e_sqlite3 2.1.12: copyright 2014-2024 SourceGear, LLC, Apache-2.0; see docs/licenses/SQLitePCLRaw-Apache-2.0.txt and https://github.com/ericsink/SQLitePCL.raw. Retain complete applicable notices in binary distributions.
