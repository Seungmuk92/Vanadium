---
description: Fix a GitHub issue following the Vanadium issue-fix workflow
argument-hint: <issue-number>
---

You are fixing GitHub issue **#$ARGUMENTS** in this repository. Follow this workflow strictly and in order. Do not skip steps.

## 1. Read the issue

Fetch issue #$ARGUMENTS (`gh issue view $ARGUMENTS`). Understand the type (bug / feature / enhancement), the reported problem, and expected behavior. Summarize your understanding before touching code.

## 2. Create a branch

Create a branch named by this rule:

- Format: `{bug|feat}/#NNN-<2-4 word english summary>`
- `bug/` for defects, `feat/` for features/enhancements.
- All lowercase, words joined with `-`.
- Examples:
  - "Bug, 노트 생성 시 오버포스팅... #102" → `bug/#102-invisible-purge-note`
  - "enhancement, SHARE 기능을 추가한다. #94" → `feat/#94-add-share`

## 3. Checkout and implement

Check out the new branch off the current base and make the fix there. Follow the code conventions in CLAUDE.md (English-only code/comments, nullable handling, async I/O, file-per-class).

## 4. Commit in logical units

- Commit in coherent logical groups — never dump all changes in one final commit.
- Small fixes where a single commit fully captures one logical change ARE allowed as one commit.
- Multiple commits are fine, but each must be a defensible logical unit you can explain.
- **Build before every commit.** Run `dotnet build Vanadium.slnx` before each commit. If it fails, do NOT commit — fix the error first, then commit only once it compiles clean (no new warnings). This guarantees every commit compiles on its own without any later "build fix" commit or history rewrite.
- **Write commit messages in Korean.** Match the existing style (e.g. `#102 <요약>`).
- **Do NOT add a `Co-Authored-By:` trailer** (or any other co-author/attribution trailer) to commit messages. Commits should be attributed to the repository owner only — no `Co-Authored-By: Claude ...` line. This keeps GitHub from displaying a second "co-authored" committer on the commit.

## 5. Push

Once all related changes are done, push the branch to `origin`.

## 6. Open a PR

Create a pull request (`gh pr create`) targeting the default branch. Title and body in Korean, referencing the issue (e.g. `Closes #$ARGUMENTS`). Summarize what changed and why.

## Verification (before pushing)

- Each commit was built with `dotnet build Vanadium.slnx` before being made (see step 4) — so a final full build should already be clean.
- If schema changed: add an EF Core migration and run `dotnet ef database update`.
