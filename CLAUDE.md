## Agent Context Pack
- Before implementing anything, read `docs/agents/README.md` and `docs/agents/architecture.md`, then ONLY the deep-dive docs the README routing table names for your task type
- Feature assignments live in `docs/agents/tasks/` — follow the task file's "Required reading" line; do not re-explore the codebase for facts those docs already state
- For balance numbers use the distilled `docs/agents/economy-balance.md`; open `docs/gameplay_loop_data.js` only for specific value lookups (the gameplay-loop HTML/JS/CSS is human-only reference)
- If your change makes any `docs/agents/` doc wrong, update that doc in the same PR (see Maintenance in `docs/agents/README.md`)

## Git Workflow
- All work merges into `develop` only via a release branch (e.g., `release/0.3`) through a PR — never commit directly to `develop` or `main`
- After running tests and committing on a feature branch, open a PR targeting the correct release branch (confirm the target before creating)
- `gh api` ruleset/branch-protection commands require GitHub Pro on private repos — do not assume they work without checking the plan (403 otherwise)

## Testing
- Always run the full Unity test suite (`scripts/test-local.ps1`) before committing
- Fix any tests that break as a result of your changes, including previously-passing regression tests
- Use the `commit-unity` skill for the full test → commit → push → PR workflow

## Unity Project Conventions
- Always verify MonoBehaviour subclasses call `base.Awake()` when overriding Awake (recurring source of bugs)
- Check that new components/services are actually instantiated in the scene and serialized fields are wired before declaring done
- When editing files, confirm we're working in the directory Unity is currently reading from (not a stale worktree)
- For ECS+MonoBehaviour code, watch for initialization race conditions between Bootstrap/SubScene and singletons
- For UI/UXML changes: grep the codebase first to confirm which UXML and USS are actually loaded at runtime before editing — avoid editing unused standalone files (e.g., edit `GameHUD.uxml`, not a standalone modal UXML that is never loaded)
- When creating materials, use the URP-appropriate shader (Unlit/Lit) to avoid magenta rendering on Android

## Verification Before Done
- After multi-file edits, run the relevant Unity tests if present
- For UI/UXML changes, verify the element is actually visible and not clipped by parent sizing (zero-height wrappers, wrong CSS class)
- For tutorial/dialogue/JSON changes, validate JSON parses and check the runtime flow end-to-end before reporting success

## Editing Etiquette
- Avoid replace_all on patterns that may match recursively—it has caused stack overflows in this codebase
- When refactoring rotation/port/direction logic, change one convention at a time and verify before chaining further edits

## Scripting / CI
- Never use em dashes (`—`), en dashes (`–`), or curly quotes in PowerShell scripts — they break parsing silently
- Never pass `-quit` to Unity in a `-runTests` invocation — it terminates Unity before tests complete and produces no results file
- Unity batchmode test runs must produce a `-testResults` XML file; if none appears, check the log before assuming tests passed
