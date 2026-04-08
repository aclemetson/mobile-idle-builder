# 13 — Beta Testing Plan

## Goals
- Validate core loop fun and retention before launch
- Identify performance issues on real devices
- Gather data on crafting balance and progression pacing

## Beta Phases

### Closed Alpha
- **Who:** 20–50 testers drawn from existing private community — no external recruitment needed
- **Focus:** Core loop, crashes, major bugs
- **Duration:** 2–4 weeks
- **Distribution:** Firebase App Distribution (Android) / TestFlight (iOS)
- **NDA:** Recommended given existing community trust — simple click-through NDA via TestFlight / Firebase

### Closed Beta
- **Who:** 200–500 testers via sign-up form
- **Focus:** Balance, onboarding, retention (Day 1/7/30)
- **Duration:** 4–6 weeks
- **Distribution:** Google Play Internal Test / TestFlight

### Open Beta
- **Who:** Public (soft launch in select markets)
- **Focus:** Server load, monetization validation, large-scale feedback
- **Duration:** 4–8 weeks
- **Distribution:** App Store / Play Store (limited regions)

## Feedback Channels
- **In-game feedback tool** — available in DEV and STAGING builds only, hidden in PROD
  - Floating bug report button on all screens
  - Captures automatic screenshot on open
  - Player can annotate screenshot, add text description, set severity
  - Submits to a dedicated AWS endpoint → stored in DynamoDB, notifies dev channel
- Discord server for testers — qualitative discussion and community feedback
- Structured survey after Week 1 and Week 4 of each beta phase

## Build Type System

### Scripting Define Symbols
Unity scripting define symbols control feature availability per build:

| Symbol | Environments | Features Enabled |
|--------|-------------|-----------------|
| `BUILD_DEV` | Dev | In-game feedback, full logging, debug overlay, cheat menu |
| `BUILD_STAGING` | Staging | In-game feedback, warning+ logging, no cheat menu |
| `BUILD_PROD` | Production | No debug features, errors only logging |

### Log Levels by Build
| Build | Log Level | What's Logged |
|-------|-----------|--------------|
| DEV | Verbose | Everything — systems, ECS, network, UI |
| STAGING | Warning | Warnings and errors only |
| PROD | Error | Errors and crashes only — sent to CloudWatch |

### Implementation Notes
```csharp
// Example usage in code
#if BUILD_DEV || BUILD_STAGING
    FeedbackButton.SetActive(true);
#endif

#if BUILD_DEV
    DebugOverlay.SetActive(true);
    CheatMenu.SetActive(true);
#endif
```
- Build type set at build time via GameCI pipeline — never manually toggled
- `BUILD_PROD` never includes any debug MonoBehaviours — stripped entirely
- AWS endpoints are environment-specific (dev/staging/prod) — defined in a config ScriptableObject selected at build time

## Key Metrics to Track
| Metric | Target |
|--------|--------|
| Day 1 Retention | > 40% |
| Day 7 Retention | > 20% |
| Session Length | > 5 min avg |
| Crash Rate | < 1% |
| Tutorial Completion | > 70% |

## Notes / Open Questions
- [ ] NDA for closed alpha?
- [ ] Compensation / rewards for beta testers?
