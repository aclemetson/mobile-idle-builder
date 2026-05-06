Implement a Unity feature with pre-flight checks to prevent the most common failure modes (missing base.Awake, unwired components, surface-level fixes that miss root cause).

## Instructions

The user will describe a feature or bug fix. Before editing any files:

1. **State the root cause** (for bug fixes): one sentence, with the evidence supporting it. If evidence is weak, add a diagnostic log first and ask the user to run it.

2. **List every file you will touch**: include SOs, Authoring components, ECS Systems, MonoBehaviour Controllers, UXML/USS, JSON data files, and asmdef files.

3. **Scene/Inspector wiring checklist**: for every new MonoBehaviour or component added, list:
   - Where it must live in the scene hierarchy
   - Every serialized field that needs wiring in the Inspector
   - Any asmdef references that need updating

4. **Acceptance criteria**: list the runtime behaviors that confirm it works (not just "it compiles").

5. **Wait for user approval** before editing anything.

After implementing:
- Verify all MonoBehaviour subclasses that override Awake call `base.Awake()`
- Validate any modified JSON files parse correctly
- Output a final "Scene Setup Checklist" summarizing what the user must do manually in the Unity Editor before hitting Play
