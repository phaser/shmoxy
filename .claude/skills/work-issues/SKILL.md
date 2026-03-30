---
name: work-issues
description: Work through open GitHub issues one by one — read each issue, create a PR branch in a worktree, write tests, fix the code, build, commit, create a PR, and move on to the next issue.
argument-hint: [issue-number-or-blank-for-all]
---

# Work Through GitHub Issues

Work through open GitHub issues sequentially. If an issue number is provided ($0), start with that issue. Otherwise, fetch all open issues and work through them in order.

## Before Starting

1. Read `AGENTS.md` and follow all instructions

## For Each Issue

### 0. Sync Main

Before starting work on any issue, always sync main first:

```bash
git checkout main && git pull origin main
```

This ensures the worktree branch is always created from the latest main.

### 1. Understand the Issue

- Fetch the issue: `gh issue view <number> --repo phaser/shmoxy`
- Read all relevant source code before making changes
- Identify the root cause

### 2. Create PR Branch (Worktree Workflow)

```bash
./scripts/new-pr.sh <short-name> "<description>"
cd <worktree-path>
```

All work happens in the worktree — never modify main directly.

### 3. Write a Failing Test First

- Create or update a test that reproduces the bug or validates the new behavior
- Follow existing test patterns and the test file organization rules in AGENTS.md

### 4. Fix the Code

- Make the minimal change that fixes the issue
- Fix all compiler warnings (zero warnings policy)

### 5. Verify

- `dotnet build` — must succeed with zero warnings
- Run the **entire** test suite, not just new tests:
  - `dotnet test tests/shmoxy.tests`
  - `dotnet test tests/shmoxy.api.tests`
  - `dotnet test tests/shmoxy.frontend.tests` (includes both unit tests and Playwright e2e)
- `nix build .#shmoxy` — must succeed

### 6. File Issues for Spotted Problems

While working on an issue, if you notice any unrelated problems (bugs, code smells, missing error handling, TODOs, etc.) that are outside the scope of the current issue:

- Create a new GitHub issue for each spotted problem: `gh issue create --repo phaser/shmoxy --title "<short description>" --body "<details of the problem and where it was found>"`
- Do NOT fix these problems in the current PR — keep the PR focused on the original issue
- Mention in the PR body that related issues were filed, with their numbers

### 7. Commit and Create PR

- Ensure the PR documentation file in `docs/prs/` is included in your commits (it is auto-committed by `new-pr.sh`, but update it with status/notes and stage any changes)
- Commit with a descriptive message referencing the issue (e.g., "Closes #N")
- Push branch and create PR via `gh pr create`
- PR body must include: Summary, Test plan, and `Closes #N`

### 8. Merge, Clean Up, and Move On

**Never stop. Never ask. Just merge and continue.**

1. Show the PR URL to the user
2. Immediately merge the PR: `gh pr merge <number> --squash --delete-branch`
3. Update main: `git checkout main && git pull origin main`
4. Clean up the worktree: `./scripts/cleanup-pr.sh <pr-name>`
5. Immediately move to the next issue — no pause, no confirmation

**Important:** When merging any PR (whether from this workflow or manually requested), always check if there is an associated local worktree for that PR branch. If one exists, clean it up after merging using `./scripts/cleanup-pr.sh <pr-name>` since it is no longer needed.

### 9. Keep Going

- After merge + cleanup, immediately pick up the next open issue
- Repeat from step 0 (sync main)
- **Do not stop between issues. Do not ask for permission. Do not wait.**
- When there are no more open issues, report that all issues are done
