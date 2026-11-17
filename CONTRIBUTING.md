# Contributing

This is a personal project, published source-available (see [LICENSE](LICENSE)), so **feature pull requests aren't being accepted.** Bug reports and suggestions are welcome as [issues](https://github.com/aclemetson/mobile-idle-builder/issues/new/choose).

The rest of this file documents how work moves through the repository, for reviewers and for the author's own tooling.

## Branch flow

```
feat/* or fix/*  ->  integration/<date>  ->  release/<major.minor>  ->  develop  ->  main
```

- Feature and fix branches are cut from the newest `integration/<date>` branch, and their PRs target it.
- An integration branch collects a batch of tested features and is promoted to the current `release/*` branch by PR.
- A release branch merges down to `develop`, and `develop` is squash-merged to `main` for each public release.
- Nobody commits directly to `integration/*`, `release/*`, `develop` or `main`.

## Before every commit

1. Run the full test suite: `.\scripts\test-local.ps1 -TestPlatform editmode` with the Editor closed, or the Test Runner or MCP `run_tests` with the Editor open. Never run batchmode tests while the Editor has the project open.
2. If you changed `Assets/Data/game_data.json`, re-run **MobileIdleBuilder > Import Game Data** and commit the regenerated assets with it.
3. If you changed a system covered by `docs/agents/`, update that doc in the same PR (see its Maintenance section).
4. Commit scene and asset wiring (`.unity`, `.asset`) with the code that needs it.

The git `pre-commit` hook bumps the build version on every commit.

## Code conventions

- MonoBehaviour singletons that override `Awake()` call `base.Awake()`.
- Managed code never touches ECS singletons before `ECSLoadBridge.IsLoaded`.
- Player-facing timers read `GameClock.UtcNow`, never `DateTime.UtcNow`.
- Before editing a UXML or USS file, grep that it's the one loaded at runtime (`GameHUD.uxml`).
- PowerShell scripts stay plain ASCII: no smart quotes or em/en dashes.

[`CLAUDE.md`](CLAUDE.md) and [`docs/agents/`](docs/agents/README.md) have the full list and the reasons behind each rule.

## Commit and PR style

- Commit subjects say what changes for the player or developer, in the imperative ("Price the Entropy Surge from a steady rate").
- PRs explain the problem, the change and how it was tested, and link the issue they close.
