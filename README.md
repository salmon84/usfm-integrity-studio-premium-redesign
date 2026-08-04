# USFM Integrity Studio Premium Redesign

USFM Integrity Studio is an offline-first desktop tool for reviewing DOCX
scripture structure, standardizing DOCX copies, converting selected books to
USFM, generating BTTW-compatible project packages, and cleaning existing USFM
or `.tstudio` files.

This repository contains the isolated Premium UI redesign. Its scripture
processing engine remains aligned with the tested Premium engine; redesign
work is confined to presentation and workflow controls unless a change is
explicitly covered by regression tests.

## Highlights

- Scan a DOCX file or chapter-folder input before conversion.
- Detect and confirm mapped Bible books while keeping manual selection
  authoritative.
- Standardize a new DOCX copy without overwriting the source.
- Preserve explicit chapter and verse numbering during DOCX-to-USFM conversion.
- Generate split USFM files and optional BTTW `.tstudio` project packages.
- Clean USFM and BTTW project files through separate, clearly labeled actions.
- Confirm the selected file identity before a cleaning job begins.
- Preserve BTTW 1.6 canonical chunk boundaries and report additional chunk
  splits without silently deleting scripture text.
- Normalize selected punctuation, quotation, whitespace, and unsafe-control
  issues with idempotence checks for Arabic-derived and other scripts.
- Validate book identity, chapter/verse structure, project manifests, chunk
  references, and duplicate verse coverage.
- Process files locally; the UIS application itself does not require a server
  login.
- Display version, source revision, build channel, repository, license, and
  offline privacy status through **About & Verify**.

## Repository layout

- `Premium/`: Avalonia desktop application.
- `Shared/`: shared scripture punctuation normalizer.
- `Tests/PunctuationRegressionTests/`: synthetic regression suite for cleaner,
  punctuation, chunk-map, and duplicate-coverage behavior.
- `REDESIGN_NOTES.md`: isolation and safety boundary for the redesign.

Generated `bin`, `obj`, `publish`, `dist`, application packages, user projects,
and local application data are intentionally excluded.

## Requirements

- .NET 10 SDK
- macOS, Windows, or Linux supported by Avalonia 11

## Build and run

```bash
dotnet restore Premium/UsfmIntegrityStudio.csproj
DOTNET_CLI_TELEMETRY_OPTOUT=1 AVALONIA_TELEMETRY_OPTOUT=1 \
  dotnet run --project Premium/UsfmIntegrityStudio.csproj
```

## Verify

```bash
DOTNET_CLI_TELEMETRY_OPTOUT=1 AVALONIA_TELEMETRY_OPTOUT=1 \
  dotnet build Premium/UsfmIntegrityStudio.csproj

DOTNET_CLI_TELEMETRY_OPTOUT=1 AVALONIA_TELEMETRY_OPTOUT=1 \
  dotnet run --project Tests/PunctuationRegressionTests/PunctuationRegressionTests.csproj
```

The regression suite uses synthetic fixtures only. Do not add private
translation projects or source texts to this repository.

## Optional source comparison

Some context-aware quote cleanup can compare a project target with a local
English source USFM. Set `UIS_SOURCE_TEXT_ROOT` to a directory containing:

```text
Source Text (eng) USFM files/en_ulb/
```

This setting is optional. No personal filesystem path is embedded in the
source.

## Safety model

- Inputs are not overwritten by normal scan, standardize, convert, or clean
  workflows.
- Explicit source verse numbers remain authoritative in preservation mode.
- Project-integrity findings are reported; noncanonical extra chunk splits are
  warnings rather than automatic scripture deletion.
- Duplicate verse coverage is blocked to avoid silently producing ambiguous
  project content.
- A second cleanup pass is expected to make no further normalization changes.

Always inspect generated files and reports before replacing production data or
uploading a project.

## License and branding

Source code is licensed under `AGPL-3.0-or-later`; see `LICENSE` and
`Premium/NOTICE`.

The code license does not grant trademark rights. See
`Premium/TRADEMARK_POLICY.md` for the branding terms applying to the Digital
Global Village and USFM Integrity Studio names and logo.

Additional governance and assurance documents:

- `COPYRIGHT.md`: ownership scope and BTT-Writer-derived-data provenance.
- `THIRD_PARTY_NOTICES.md`: direct and transitive dependency notices.
- `PRIVACY.md`: offline processing and network behavior.
- `SECURITY.md`: private vulnerability reporting.
- `CONTRIBUTING.md`: testing, private-data restrictions, and DCO sign-off.
- `OFFICIAL_RELEASES.md`: official-build identity and release verification.
- `LEGAL_AND_RELEASE_AUDIT.md`: implemented controls and residual owner actions.
- `CHANGELOG.md`: versioned public change history.
