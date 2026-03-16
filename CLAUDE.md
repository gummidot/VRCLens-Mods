# Project Instructions

## Plan Mode

When operating in **plan mode**, always save the plan as a Markdown file in `.github/prompts/`.

### File naming

`YYYY-MM-DD-plan-<short-topic>.prompt.md`

- Use the current date for `YYYY-MM-DD`.
- `<short-topic>` should be a concise kebab-case slug describing the plan (e.g., `drone-roll-modifier`, `blur-enhancement`).

### File format

Follow this template (see existing files in `.github/prompts/` for real examples):

```markdown
# Plan: <Title>

**Date:** YYYY-MM-DD
**Status:** Not started
**Complexity:** Low | Medium | High

<One-paragraph summary: what the change does and why.>

**Target file(s):** <paths>

---

## Background

<Context, motivation, relevant technical details.>

## Decisions

<Key design choices and their rationale.>

## Steps

1. <Actionable step with file paths and symbol references>
2. …

## Verification

<How to confirm correctness: commands, tests, manual checks.>
```

### Rules

- **Always** write the plan file — do not only present the plan in chat.
- Keep the plan self-contained: someone reading only the file should be able to implement it.
- Reference concrete file paths, symbol names, and line numbers where relevant.
- Update `**Status:**` as work progresses (`Not started` → `In progress` → `Implemented`).
- **Always** update the plan file when the plan changes — the file is the source of truth, not the chat history.
