# Contributing

Contributions are welcome through GitHub pull requests.

## Required checks

Before submitting:

```bash
dotnet build Premium/UsfmIntegrityStudio.csproj
dotnet run --project Tests/PunctuationRegressionTests/PunctuationRegressionTests.csproj
```

Every behavioral defect should receive a synthetic regression case. Do not add
private translation content, user projects, credentials, generated packages,
`bin`, `obj`, or local application data.

## Developer Certificate of Origin

Every commit must include a `Signed-off-by` line certifying that the contributor
has the right to submit the work under AGPL-3.0-or-later. Use:

```bash
git commit -s
```

The sign-off follows the Developer Certificate of Origin 1.1:
<https://developercertificate.org/>

The sign-off is a provenance declaration, not a copyright assignment. A
separate written contributor agreement may be required before accepting work
intended for dual-licensed or trademark-sensitive distributions.

## Branding

Code contributions do not grant permission to publish modified products as
official Digital Global Village or USFM Integrity Studio releases. See
`Premium/TRADEMARK_POLICY.md`.

## Review priorities

Scripture identity, verse anchoring, metadata integrity, offline privacy, and
import/export compatibility take priority over implementation convenience.
