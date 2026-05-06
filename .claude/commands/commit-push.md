Stage, commit, and push the current branch with a conventional commit message.

## Instructions

1. Run `git status` to see what's changed.
2. Run `git diff` to understand what the changes do.
3. Stage only relevant files — no `.env`, secrets, or large binaries.
4. Write a conventional commit message:
   - `feat(scope): ...` for new features
   - `fix(scope): ...` for bug fixes
   - `refactor(scope): ...` for refactors
   - `chore(scope): ...` for maintenance
   - Keep the subject line under 72 characters
   - Add a short body if the *why* isn't obvious from the title
5. Commit (do not amend).
6. Push to the current branch with `git push`.
7. Report the commit hash and branch pushed to.
