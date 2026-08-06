---
id: sample.code-review
name: code-review
description: Review code for correctness, regressions, security risks, and missing tests.
tags: [review, code, testing, security]
---

# Code Review

Use this skill when the user asks for a review, PR check, pre-merge inspection, or risk assessment of code changes.

## Instructions

- Lead with findings, ordered by severity.
- Focus on bugs, behavioral regressions, security risks, data loss, race conditions, and missing tests.
- Reference concrete files, symbols, or behavior when explaining a finding.
- Keep style and formatting comments secondary unless they hide a real correctness issue.
- If no issues are found, say that clearly and mention any test gaps or residual risk.

## Output Shape

Start with findings. Then include open questions or assumptions if needed. Put a short summary after the findings, not before them.
