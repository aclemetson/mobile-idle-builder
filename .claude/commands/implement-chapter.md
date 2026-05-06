Implement the next pending chapter from the project outline, or a specific chapter if one is provided as an argument.

## Instructions

### Step 1 — Identify the target chapter

Read `.claude/outline-files/README.md` to find the project status table.

If an argument was passed (e.g. `/implement-chapter 15`), use that chapter. Otherwise, find the lowest-numbered chapter that is unchecked (`- [ ]`) and not marked PARKED.

State which chapter you are targeting and why (next pending, or user-specified).

---

### Step 2 — Read the chapter outline

Read `.claude/outline-files/[NN]-[slug].md` for the target chapter.

Also read `ScriptableObject-Schemas.md` if the chapter involves new data types or ScriptableObjects.

---

### Step 3 — Survey the current codebase

Before planning, read the existing code relevant to this chapter. At minimum:
- Scan `Assets/Scripts/` for any files that overlap with the chapter's scope
- Check `Assets/Data/` for any existing JSON or ScriptableObject assets
- Check `Assets/Scripts/Tests/` for any existing tests covering this area

The goal is to understand what already exists so the plan doesn't duplicate or contradict it.

---

### Step 4 — Present the implementation plan

Output the following before touching any files:

**Chapter N — [Title]**

**Files to create or modify:**
List every file: Components, Systems, Authoring, ScriptableObjects, UI (UXML/USS), JSON data, asmdef updates, test files.

**Scene/Inspector wiring required:**
List every new MonoBehaviour/component, where it lives in the hierarchy, and every serialized field the user must wire in the Inspector after implementation.

**Review gate:** State YES or NO.
Automatically set to YES if the chapter touches any of:
- Save data serialization or cloud sync (SaveData fields, ICloudSaveService)
- IAP, payments, or monetization logic
- Deletion of existing systems or components
- Networking or backend API changes

If review gate is YES, explain what specifically needs human sign-off.

**Acceptance criteria:**
List the runtime behaviors that confirm the chapter works — what the user should see in the editor when it's done.

**Test checklist (for Unity editor verification):**
List the test cases to run in the Unity Test Runner after implementation.

---

### Step 5 — Wait for approval

Stop and wait. Do not edit any files until the user explicitly approves the plan. If the review gate is YES, call that out prominently.

---

### Step 6 — Implement

Create the branch and implement the chapter:

```bash
git checkout -b chapter-[NN] main
```

Work through the file list from the plan. Follow all rules from CLAUDE.md:
- Verify every MonoBehaviour subclass that overrides Awake calls `base.Awake()`
- Validate any modified JSON files parse correctly (jq will auto-check via hook)
- Change one convention at a time for rotation/direction/port logic
- Do not use replace_all on patterns that may match recursively

Checkpoint commit after every 5 file changes:
```bash
git add -A && git commit -m "chore(chapter-[NN]): checkpoint"
```

---

### Step 7 — Commit and push

When implementation is complete:

```bash
git add -A
git commit -m "$(cat <<'EOF'
feat(ch[NN]): [chapter title summary]

[2-3 sentence description of what was implemented and key decisions]
EOF
)"
git push -u origin chapter-[NN]
```

---

### Step 8 — Open a draft PR

```bash
gh pr create \
  --base main \
  --head chapter-[NN] \
  --title "feat(ch[NN]): [chapter title]" \
  --draft \
  --body "$(cat <<'EOF'
## Chapter [NN] — [Title]

## What was implemented
[bullet points from the plan]

## Scene setup required
[wiring checklist from the plan — what the user must do in the Unity Editor]

## Unity Test Runner checklist
[test cases from the plan — run these in the editor before merging]

## Files changed
[list of key files]

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

### Step 9 — Update the README

Edit `.claude/outline-files/README.md`: change the chapter's `- [ ]` to `- [x]` and append a brief description of what was implemented (matching the style of the existing completed entries).

Commit this change:
```bash
git add .claude/outline-files/README.md
git commit -m "chore: mark chapter [NN] complete in README"
git push
```

---

### Step 10 — Summary

Report:
- PR URL
- Files created/modified (count)
- Scene setup checklist (what the user must do before hitting Play)
- Test checklist (what to verify in the Unity Test Runner)
- Which chapter is next in the queue
