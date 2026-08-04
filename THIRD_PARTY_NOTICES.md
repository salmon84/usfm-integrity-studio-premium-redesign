# Third-party notices

This file summarizes material dependencies resolved by the application. It is
not a substitute for the complete license text shipped by each dependency.

## BTT-Writer Desktop canonical chunk data

- Component: canonical chunk boundaries generated from BTT-Writer Desktop 1.6
  build 1073
- Upstream: <https://github.com/Bible-Translation-Tools/BTT-Writer-Desktop>
- Copyright notice in upstream: Copyright (C) 2019 Wycliffe Associates
- License: GPL-2.0-or-later
- Use here: BTTW-compatible chunk mapping and project validation

The GPLv2 license text is included at `LICENSES/GPL-2.0-or-later.txt`.

No affiliation with or endorsement by Wycliffe Associates is implied.

## Direct NuGet dependencies

- Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent,
  Avalonia.Diagnostics, and Avalonia.Fonts.Inter 11.3.11: MIT
- CommunityToolkit.Mvvm 8.2.1: MIT
- Tmds.DBus.Protocol 0.21.3: MIT; explicitly pinned to the patched 0.21
  release for GHSA-xrw6-gwf8-vvr9

## Resolved transitive components

The current restore graph includes Avalonia platform packages, SkiaSharp,
HarfBuzzSharp, MicroCom.Runtime, and Tmds.DBus.Protocol under MIT-compatible
terms. Avalonia.Angle.Windows.Natives includes ANGLE under a BSD-style
three-clause license.

Exact transitive versions can vary by target runtime and restore date. Release
builders must inspect `obj/project.assets.json`, retain package-provided license
files, include this notice and the `LICENSES/` directory, and regenerate this
notice when dependency versions change.

## Standards and names

USFM is referenced descriptively as a scripture-markup format. BTT-Writer,
Wycliffe Associates, Avalonia, Microsoft, .NET, Inter, Skia, HarfBuzz, and other
third-party names remain the property of their respective owners. Their mention
does not imply sponsorship or endorsement.
