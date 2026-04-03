---
name: team
description: Create a coordinated shmoxy agent team (manager, programmer, qa) with shared tasks and messaging.
argument-hint: <agent(s)> <instruction> — e.g. "all do your thing", "programmer fix issue #42", "qa,manager review the proxy server"
---

# Team Dispatcher

Create and coordinate an agent team for shmoxy. Parse the user's arguments ($0) to determine which agents to spawn as teammates and what instruction to give them.

## Agents Available

| Keyword        | Agent type       | Description |
|---------------|------------------|-------------|
| `manager`     | manager          | Product manager — competitive analysis, feature gaps, UX issues, files GitHub issues |
| `programmer`  | uber-programmer  | Elite programmer — implements features, fixes bugs, writes tests, creates PRs |
| `qa`          | qa-specialist    | QA engineer — tests features, finds bugs, identifies test gaps, files issues |
| `all` / `team` / `start` | all three | Spawns all three teammates |

## Parsing Rules

1. The first word(s) of the argument select the agent(s). Accept comma-separated (`qa,manager`) or space-separated before the instruction.
2. Everything after the agent selector is the instruction passed to each agent.
3. If no specific agent is named, or the keyword is `all`, `team`, or `start`, dispatch to **all three** agents.
4. If only an instruction is given with no recognizable agent keyword, dispatch to **all three** with that instruction.

### Examples

- `/team all do your thing` → all three teammates, instruction: "do your thing"
- `/team start` → all three teammates, instruction: "do your thing" (default)
- `/team programmer fix the WebSocket frame parser` → programmer teammate only
- `/team qa,manager review the inspection hook` → qa and manager teammates
- `/team manager` → manager teammate only, instruction: "do your thing" (default)

## Workflow

### Step 1: Create the Team

Use **TeamCreate** to create a coordinated team:
- `team_name`: `"shmoxy-crew"` (or reuse if it already exists)
- `description`: derived from the user's instruction

This creates a shared task list and team config that all teammates can access.

### Step 2: Create Tasks

Use **TaskCreate** to populate the shared task list with work items for each selected agent. Each task should:
- Have a clear title and description based on the user's instruction
- Be scoped to the agent that will own it

For "do your thing" (default instruction), create these tasks:
- **Manager**: "Competitive analysis — compare shmoxy against Zap, Burp Suite, and Charles Proxy. Identify feature gaps, UX issues, and file GitHub issues for actionable findings."
- **Programmer**: "Code quality sweep — find and fix bugs, address compiler warnings, improve code quality, ensure tests pass."
- **QA**: "Test coverage audit — test existing features, find bugs, identify testing gaps, and file issues for missing tests."

For custom instructions, create tasks that reflect what the user asked for.

### Step 3: Spawn Teammates

For each selected agent, spawn it using the **Agent tool** with:
- `subagent_type` matching the agent type from the table
- `team_name`: `"shmoxy-crew"`
- `name`: the agent keyword (e.g., `"manager"`, `"programmer"`, `"qa"`)
- A prompt that includes:
  1. The user's instruction (or the default task description)
  2. A reminder to read AGENTS.md first
  3. The project path: `/Users/phaser/projects/shmoxy`
  4. Instructions to check TaskList for assigned tasks and mark them completed via TaskUpdate
  5. Instructions to message teammates via SendMessage when sharing findings or needing coordination

**Always spawn independent teammates in parallel** (multiple Agent tool calls in a single message).

**Do NOT use `run_in_background`** — let the team system manage teammates so they get proper tmux panes (configured via `teammateMode: "tmux"` in `~/.claude.json`).

If the agent keyword is `programmer` and the instruction involves code changes, use `isolation: "worktree"` so the programmer works in an isolated copy.

### Step 4: Assign Tasks

After spawning, use **TaskUpdate** with `owner` to assign each task to the appropriate teammate by name.

## After Dispatch

Report which teammates were spawned and their assigned tasks. As the team lead:
- Monitor teammate progress via messages (they arrive automatically)
- Reassign or create new tasks as needed
- Synthesize findings when teammates complete their work
- When all work is done, shut down teammates via SendMessage with `message: {type: "shutdown_request"}` and clean up the team

## Team Coordination Notes

- Teammates share a task list at `~/.claude/tasks/shmoxy-crew/`
- Teammates can message each other directly via SendMessage
- Idle teammates can receive messages — idle is normal, not an error
- The team config is at `~/.claude/teams/shmoxy-crew/config.json`
- Do NOT use terminal tools to check teammate activity — use SendMessage instead
