# Official releases and build verification

## Official distribution

An official build must:

1. be published from the `salmon84/usfm-integrity-studio-premium-redesign`
   repository's GitHub Releases page;
2. identify an exact Git tag and source commit;
3. display the same commit in **About & Verify**;
4. be built with `BuildChannel=official` and `OfficialBuild=true`;
5. publish SHA-256 checksums and a dependency manifest or SBOM for every
   release;
6. pass the repository build and regression workflow; and
7. include `LICENSE`, `LICENSES/`, `COPYRIGHT.md`,
   `THIRD_PARTY_NOTICES.md`, `PRIVACY.md`, and `Premium/NOTICE`; and
8. be code-signed when platform signing credentials are available.

The tag-triggered release workflow builds native packages on GitHub-hosted
macOS, Windows, and Linux runners. Packaging does not modify the scripture
engine. Until Apple and Windows signing credentials are configured, release
notes and the README must identify the packages as unsigned/not notarized.

A label inside an executable is not cryptographic proof. Verify the package's
signature and checksum against the official release page.

## Community and development builds

Local source runs, pull-request artifacts, forks, and packages missing the
official release evidence must identify themselves as development, community,
or unofficial builds. They must not imply endorsement.

## Release build properties

Release builders should supply immutable source identity:

```bash
dotnet publish Premium/UsfmIntegrityStudio.csproj \
  -c Release \
  -p:SourceRevisionId="$(git rev-parse --verify HEAD)" \
  -p:BuildChannel=official \
  -p:OfficialBuild=true
```

The source revision must match the tagged commit. Release packages and
checksums must be retained with the GitHub release.

## Package formats

- macOS Apple Silicon and Intel: `.dmg` and zipped `.app`
- Windows x64: Inno Setup installer and portable `.zip`
- Linux x64: Debian `.deb` and portable `.tar.gz`

## Monitoring

Repository Insights, public forks, code search, Dependabot, and release records
provide visibility into public activity. They cannot identify every private
clone or lawful private modification. No hidden application tracking is used.
