# Changelog

All notable changes to this project will be documented here.

## 0.1.0 - 2026-08-05

### Added

- Transparent **About & Verify** build identity and privacy information.
- AGPL, BTT-Writer provenance, third-party, privacy, security, contribution,
  trademark, and official-release policies.
- Locked NuGet dependency graph, advisory auditing, CI, Dependabot, and
  CODEOWNERS.
- GitHub branch protection and private vulnerability reporting.

### Security

- Pinned `Tmds.DBus.Protocol` to patched version 0.21.3 for
  GHSA-xrw6-gwf8-vvr9.
- Removed the Avalonia default application icon from UIS branding.

### Distribution

- Added reproducible macOS Apple Silicon/Intel, Windows x64, and Linux x64
  release packaging with official source-revision metadata.
- Added SHA-256 checksums, dependency-lock evidence, and legal notices to the
  release packages.
- Documented that initial packages are unsigned and not Apple-notarized.
