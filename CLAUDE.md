## Unity Project Conventions
- Always verify MonoBehaviour subclasses call `base.Awake()` when overriding Awake (recurring source of bugs)
- Check that new components/services are actually instantiated in the scene and serialized fields are wired before declaring done
- When editing files, confirm we're working in the directory Unity is currently reading from (not a stale worktree)
- For ECS+MonoBehaviour code, watch for initialization race conditions between Bootstrap/SubScene and singletons

## Verification Before Done
- After multi-file edits, run the relevant Unity tests if present
- For UI/UXML changes, verify the element is actually visible and not clipped by parent sizing (zero-height wrappers, wrong CSS class)
- For tutorial/dialogue/JSON changes, validate JSON parses and check the runtime flow end-to-end before reporting success

## Editing Etiquette
- Avoid replace_all on patterns that may match recursively—it has caused stack overflows in this codebase
- When refactoring rotation/port/direction logic, change one convention at a time and verify before chaining further edits
