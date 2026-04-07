# 06 — CI/CD Pipeline

## Overview
Automate building, testing, and deploying the game so every change is validated quickly and releases are reliable.

## Recommended Stack
| Tool | Role |
|------|------|
| GitHub Actions | Primary CI/CD runner |
| GameCI | Unity-specific build automation (primary) |
| Unity Build Cloud | Fallback if GameCI insufficient |
| Firebase App Distribution | Internal test builds |
| App Store Connect / Google Play | Production releases |

### GameCI Notes
- Open source, runs Unity builds inside GitHub Actions using a personal Unity license
- No additional cost beyond GitHub Actions compute minutes
- Supports Android (.aab / .apk) and iOS (.ipa) builds
- If GameCI proves insufficient (license issues, build complexity), migrate to Unity Build Cloud

## Pipeline Stages

```
Push to develop / release/* (merge only)
  └─> Run Unit & Integration Tests (every PR)
        └─> Build (iOS + Android) — on merge to develop only
              └─> Deploy to Firebase (internal testers)
                    └─> Manual approval → Production release
```

### Trigger Rules
| Event | Tests | Build |
|-------|-------|-------|
| PR opened / updated | ✅ Run | ❌ Skip |
| Merge to `develop` | ✅ Run | ✅ Build |
| Merge to `release/*` | ✅ Run | ✅ Build |
| Merge to `main` | ✅ Run | ✅ Build + deploy |
| Feature branch push | ❌ Skip | ❌ Skip |

- Keeps GitHub Actions minutes conservative — Unity builds only trigger on meaningful merges
- Tests still run on every PR to catch issues early without burning build minutes

## Branch Strategy
- `main` — stable, always deployable
- `develop` — integration branch
- `feature/*` — individual features
- `release/*` — release candidate branches

## Build Targets
- Android: `.aab` for Play Store, `.apk` for internal testing
- iOS: `.ipa` via Xcode Cloud or GameCI

## Secrets Management
- API keys and signing certs stored as GitHub Secrets
- Never committed to the repo

## Notes / Open Questions
- [ ] Set up Unity license secret in GitHub for GameCI
- [ ] Define iOS signing cert storage strategy (GitHub Secrets + fastlane match recommended)
