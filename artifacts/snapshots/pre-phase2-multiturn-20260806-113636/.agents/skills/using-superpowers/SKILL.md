---
name: using-superpowers
description: Bootstrap skill — teaches the agent how to find and invoke skills. Use when starting any new task or session.
---

# Using Superpowers

## The Core Rule

If there is even a **1% chance** a skill applies to the current task, invoke it.

Skills are cheap to load. Failing to use an applicable skill is expensive.

## How to Find Skills

At the start of every new task:

1. Review available skills (check `~/.openclaw/extensions/superpowers/skills/`)
2. Ask: "Does any skill match what I'm about to do?"
3. If yes → load and follow the skill exactly
4. If no → proceed with default behavior

In OpenClaw's persistent agent context, check skills at the start of **every new task**, not just new conversations. A long-running session may handle many different task types.

## Decision Flowchart

```
New task received
    ↓
Does any skill apply? (even 1% chance → YES)
    ↓
YES → Announce: "Using [skill-name] to [purpose]"
      Load skill file
      Follow instructions exactly
      ↓
      Complete or hand off cleanly

NO → Proceed with default agent behavior
```

## Priority Hierarchy

1. **User instructions** — always highest priority
2. **Skills** — override default behavior when applicable
3. **Default behavior** — fallback when no skill applies

## Announcing Skill Usage

When you invoke a skill, say so:

> "Using `brainstorming` to explore approaches before writing code."
> "Using `systematic-debugging` to diagnose this error."

This keeps the human informed and lets them redirect if needed.

## OpenClaw-Specific Notes

- In persistent sessions, skills should be checked at the start of each new task, not just session start
- Skills around `task-handoff` and `agent-self-recovery` matter more in OpenClaw than in session-based tools — don't skip them
- If a task will take more than ~20 minutes, check `long-running-task-management` first
