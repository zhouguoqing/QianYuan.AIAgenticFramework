---
name: brainstorm
description: Collaborative discovery and design framing for ambiguous or high-risk work. Use when requirements are unclear, multiple approaches are possible, or you need to turn an idea into a validated design brief before planning or coding.
argument-hint: "[brief description of the idea or problem to solve]"
license: MIT
---

# Brainstorm

## Overview

Use this skill to convert rough ideas into clear, reviewable design outputs through structured dialogue.

The goal is to:

1. Clarify requirements and constraints
2. Explore alternatives with trade-offs
3. Produce a concrete, validated design brief in artifacts or a handoff to planning

## Workflow

### Step 1: Gather Project Context

Load only the project context relevant to the current idea:

- If `docs/SUMMARY.md` exists, read it first.
- Load only task-relevant detail docs.
- Prioritize `Code Standard` docs for implementation conventions.
- If docs conflict with code or user intent, use the available input/question tool before broad changes.

Also check key implementation files relevant to the idea and note constraints from existing architecture, dependencies, and conventions.

Keep this pass focused. Only gather what is needed for the current idea.

### Step 2: Clarify Requirements

Ask targeted questions sequentially to remove ambiguity with input/question tool:

- Focus on:
  - Objective and user value
  - Scope boundaries
  - Constraints (technical, UX, performance, timeline)
  - Success criteria
  - Non-goals

Do not jump to implementation details too early.

### Step 3: Explore Approaches

Propose 2-5 viable approaches.

For each approach, include:

- Short summary
- Pros
- Cons / risks
- Complexity estimate
- Recommended use conditions

Lead with your recommended option and explain why it best fits the project context and constraints.

After presenting all approaches, use input/question tool to let the user pick their preferred approach. List the summary options. Example:

1. Approach A, short summary
2. Approach B, short summary
3. Approach C, short summary
4. Other (please specify)

with the tag added for the recommended approach to guide the user.

### Step 4: Present the Design Incrementally

Once requirements are clear, present the design incrementally in logical phases (about 200-300 words per phase) to avoid overwhelming the user.

- **Phase 1: Foundation** - Problem framing, goals, and proposed architecture/flow.
- **Phase 2: Technical Details** - Data model, interfaces, error handling, and edge cases.
- **Phase 3: Delivery** - Testing/verification strategy and rollout considerations (if applicable).

After presenting **each phase**, use input/question tool immediately to ask whether to:

1. Proceed to the next phase
2. Adjust the current phase
3. Revisit a previous phase

### Step 5: Close the Loop

After you and the user have worked through requirements and the design is validated, determine the next actions.

1. Use input/question tool to present the user with three high-level next actions:
   - "Write plan immediately (in current context)" - skip the artifact step and move straight to a `write-plan` handoff.
   - "Write artifacts" - continue by authoring the brainstorm documents described in Step 6.
   - "Implement immediately (skip design artifacts and planning)" - if the design is very clear and low-risk, the user may choose to skip both artifacts and planning and move straight to implementation.
   - "End session (already provided enough information for user)" - stop; the conversation has produced enough insight for now.

2. If the user picks **Write artifacts**, proceed to Step 6. Once the draft artifacts exist, use input/question tool again to validate them with options:
   - "Write plan with current artifacts, context"
   - "End session - artifacts are sufficient for now"
   - "Need changes" (free-form text) - collect the feedback, revise the artifacts, and re-ask.

3. If the user picked **Write plan immediately**, initiate a handoff to use skill `write-plan` using the current brainstorming context; no additional artifact validation is required.

4. If the user picked **End session**, simply stop. The information collected so far is considered sufficient.

### Step 6 (optional): Write Brainstorm Artifacts

Only perform this step after the user has explicitly chosen "Write artifacts" during Step 5.

Persist results to the standardized location:

- Directory: `docs/.brainstorms/YYMMDD-HHmm-<topic-slug>/`
- Main file (required): `docs/.brainstorms/YYMMDD-HHmm-<topic-slug>/SUMMARY.md`
- Optional supporting files:
  - `docs/.brainstorms/YYMMDD-HHmm-<topic-slug>/section-01-<slug>.md`
  - `docs/.brainstorms/YYMMDD-HHmm-<topic-slug>/section-02-<slug>.md`
  - etc.

`SUMMARY.md` format: strictly follow the template inside `references/summary-template.md`.

## Rules

- Do not write production code or make implementation changes in this skill in the brainstorm session.
- Keep interaction lightweight and iterative; every step should be run in the same session.
- Prefer clarity over completeness when uncertain; ask a follow-up question.
- Align all recommendations with project documentation and standards.
- Keep assumptions explicit; do not guess silently.
- **Think before coding:** Surface assumptions explicitly. If multiple interpretations exist, present them — don't pick silently. If a simpler approach exists, say so and push back when warranted.
