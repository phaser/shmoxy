---
name: work-issues
description: Work through open GitHub issues one by one — read each issue, create a PR branch in a worktree, write tests, fix the code, build, commit, create a PR, and move on to the next issue.
argument-hint: [issue-number-or-blank-for-all]
---

# Work Through GitHub Issues

Work through open GitHub issues sequentially. If an issue number is provided ($0), start with that issue. Otherwise, fetch all open issues and work through them in order.

## Before Starting

1. Read `AGENTS.md` and follow all instructions
2. Make sure you're on the `main` branch and it's up to date: `git checkout main && git pull origin main`

## For Each Issue

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
- `dotnet test tests/shmoxy.tests` and `dotnet test tests/shmoxy.api.tests` — all tests must pass
- `nix build .#shmoxy` — must succeed

### 6. Commit and Create PR

- Commit with a descriptive message referencing the issue (e.g., "Closes #N")
- Push branch and create PR via `gh pr create`
- PR body must include: Summary, Test plan, and `Closes #N`

### 7. After PR is Created

- Tell the user the PR is ready and show the PR URL
- **Ask the user** whether to merge, or wait for review
- If told to merge: merge the PR, update main (`git pull`), clean up the worktree (`./scripts/cleanup-pr.sh`)

### 8. Move to Next Issue

- After the current issue is fully done (merged + cleaned up), immediately pick up the next open issue
- Repeat from step 1
- When there are no more open issues, report that all issues are done
