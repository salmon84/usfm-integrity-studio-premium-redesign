# Legal and release-control audit

Audit date: 2026-08-05

This document records engineering and repository-governance checks. It is not
legal advice and does not replace review by qualified counsel.

## Implemented controls

- Full AGPL-3.0 license at repository root.
- Copyright and BTT-Writer-derived-data provenance statement.
- GPLv2 license text for BTT-Writer-derived canonical chunk data.
- Direct and transitive dependency notices.
- Corrected trademark policy separating code rights from brand rights.
- Offline privacy policy and no hidden telemetry or machine identification.
- Security policy and GitHub private vulnerability reporting.
- DCO sign-off requirement and private-data contribution restrictions.
- Build version, source revision, channel, official-status, repository, license,
  and privacy metadata exposed through **About & Verify**.
- Official-release definition requiring tagged source, checksums, dependency
  manifest/SBOM, legal notices, tests, and signing when available.
- NuGet lock file, advisory auditing, CI, Dependabot, CODEOWNERS, secret
  scanning, push protection, and protected `main` history.

## Verified technical facts

- Scripture processing is local.
- No HTTP client, socket, analytics SDK, machine identifier, crash uploader, or
  automatic update service exists in the application source.
- External browsing occurs only after a user selects a website, source, or
  license link.
- BTT-Writer canonical chunk data is identified in source and notices.
- The restored dependency graph contains no known vulnerabilities reported by
  the configured NuGet sources after pinning `Tmds.DBus.Protocol` 0.21.3.

## Owner actions still required

1. Confirm and retain evidence that Digital Global Village is authorized to
   license every original UIS source file and document.
2. Confirm ownership or written permission for the Digital Global Village logo,
   USFM Integrity Studio name, and other distinctive branding.
3. Obtain contributor agreements if proprietary dual licensing or copyright
   assignment is required; DCO sign-off alone is not an assignment.
4. Obtain legal review before asserting registered trademark rights, enforcing
   against a distributor, or offering proprietary exceptions to AGPL.
5. Establish platform code-signing identities before labeling release packages
   as cryptographically verified official builds.
6. Regenerate dependency notices, lock files, audit results, checksums, and SBOM
   for every release.

## Control limitation

AGPL and repository settings do not identify every private clone or lawful
private modification. Official signatures, checksums, trademark policy, public
fork monitoring, code search, and source-offer enforcement provide evidence and
visibility; they are not covert tracking mechanisms.
