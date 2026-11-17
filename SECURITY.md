# Security Policy

## Reporting a vulnerability

Please **do not open a public issue** for security problems.

Report them privately through GitHub: **Security > Report a vulnerability** on this repository ([direct link](https://github.com/aclemetson/mobile-idle-builder/security/advisories/new)). Include what you found, how to reproduce it, and the impact you expect.

You can expect an acknowledgement within a few days. Once the issue is confirmed and fixed, the advisory is published with credit, unless you'd rather stay anonymous.

## Scope

In scope:
- The game client in this repository (save handling, purchase and ad-reward validation, authentication flow)
- The build and CI configuration in `.github/` and `scripts/`
- Any credential or secret accidentally committed to the repository

Out of scope:
- Vulnerabilities in third-party services and SDKs (Unity Gaming Services, Google Play, Apple, LevelPlay). Report those to the vendor.
- Cheating in a single-player save on your own device

## Supported versions

Only the latest release on `main` gets fixes.

## How this repository protects itself

- Secret scanning with push protection, and Dependabot alerts and security updates
- CodeQL code scanning
- Signing keys, store credentials and service-account keys are stored only as GitHub Actions secrets or on the build machine, never in the repository
- Workflow actions are pinned to commit SHAs with least-privilege permissions. Self-hosted runner jobs never run fork pull requests.
