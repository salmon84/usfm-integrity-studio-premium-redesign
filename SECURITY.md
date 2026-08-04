# Security policy

## Supported code

Security fixes are applied to the current `main` branch and the latest release,
when releases are available. Older source snapshots and unofficial forks may
not receive fixes.

## Reporting a vulnerability

Use the repository's GitHub **Report a vulnerability** function so the report
can be discussed privately. Do not open a public issue for an unpatched
security vulnerability.

Include:

- affected commit, version, and platform;
- reproducible steps using synthetic or sanitized files;
- expected and actual behavior;
- security impact; and
- suggested mitigation, if known.

Never attach private scripture projects, credentials, tokens, translator
details, or personal filesystem information.

## Scope

Relevant reports include arbitrary file access, command execution, unsafe
archive extraction, path traversal, project contamination, scripture data loss,
secret leakage, dependency compromise, and misleading official-build identity.

Formatting preferences and ordinary conversion defects should use normal
issues unless they create a confidentiality, integrity, or availability risk.

## Disclosure

Please allow maintainers time to reproduce and patch a confirmed issue before
public disclosure. No response-time or bounty commitment is made by this policy.
