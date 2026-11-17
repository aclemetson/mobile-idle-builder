# 0002: Use Unity Gaming Services instead of a custom AWS backend

**Status:** Accepted. It supersedes the original AWS design (Cognito, AppSync, API Gateway, Lambda, DynamoDB and SNS in Terraform), which was removed from the repository in release 0.5.

## Context

The first backend plan was a custom AWS stack defined in Terraform with dev, staging and prod environments. It was never wired up: the client's API base URL stayed blank and nothing issued auth tokens. Running it would have meant owning Lambda code, a schema, IAM, three environments and a monthly bill, all for a solo project whose backend needs are standard:
- player identity
- one save blob per player
- remote feature flags
- analytics
- crash reports

## Decision

Use Unity Gaming Services, which the project was already linked to:

| Need | Service |
|---|---|
| Identity | Authentication: anonymous, then Google sign-in, with session-token reuse |
| Save | Cloud Save: the full `SaveData` JSON under one key, reconciled with the local file at boot |
| Flags | Remote Config: `FeatureFlagService`, which reads remote, then cache, then default |
| Telemetry | Analytics (behind its own flag) |
| Crashes | Cloud Diagnostics |

All of these sit behind interfaces such as `ICloudSaveService`, so tests and offline play use local implementations.

## Consequences

- No servers to run. The free tier covers testing, and the integration code is small and testable.
- Kill-switches and a maintenance mode ship with no backend deploy: flip a Remote Config key.
- There is vendor lock-in to UGS, kept in check by the interfaces at every call site.
- Some setup lives in the dashboard rather than the repo: enabling products, Remote Config keys and analytics event schemas. The runbooks in `docs/runbooks/` record those steps.
- PVP leaderboards still call a configurable HTTP endpoint (`game_config.api_base_url`, empty), so they stay dormant until they're ported to UGS Leaderboards.
