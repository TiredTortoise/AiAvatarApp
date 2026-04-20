# Contributing

This repo is built by multiple coding agents working in parallel. The rules below are load-bearing — breaking them causes merge conflicts, circular asmdef references, or silent architectural drift. Read before you write.

All task IDs (T1, T5, T1.5, …) refer to [docs/design/AI_Avatar_App_Design_Doc.md](docs/design/AI_Avatar_App_Design_Doc.md), Section 10.

---

## 1. The one-way dependency rule

The module asmdefs enforce a strict one-way dependency graph:

```
UI  →  Domain (Prompt / Response / Tts / Input / Avatar / Settings)  →  Providers (shared interfaces)  →  Common
                                                                            ↓
                                                          concrete providers (Anthropic / OpenAI / Google / Local)

App (composition root) → everything
```

**Hard rules:**

- `Common` references nothing.
- Domain modules (`Prompt`, `Response`, `Tts`, `Input`, `Avatar`, `Settings`, `UI`) reference `Common` and — where listed in the design doc — `Providers` (the *shared interface* asmdef, not a concrete provider).
- Concrete provider asmdefs (`Providers.Anthropic`, `Providers.OpenAI`, …) reference `Common` and `Providers`. They do not reference each other.
- **Only `AiAvatarApp.App` may reference a concrete provider asmdef.** This is what makes provider-swapping at runtime possible.
- No asmdef may reference a higher layer. No upward or sideways references between concrete providers.

If you find yourself wanting to add a reference that breaks this graph — **stop**. Do not edit the asmdef. That's a design question, not an implementation choice: open a PR that updates the design doc first (flag it as blocking on OQ-1…n in Section 12) and get alignment before the asmdef change.

If the compiler is preventing you from doing the "obvious" thing, the obvious thing is probably wrong for the architecture.

---

## 2. Task ownership and file scope

Each task in Section 10 of the design doc has an **Owns files** column. That's the full extent of the files your PR should touch for that task.

- If your task needs to add a field/method to a shared contract that someone else owns (e.g. a new field on `ChatDelta` in `Common/Models`), you do **not** silently add it. Either: (a) update the design doc first, or (b) open a small coordination PR for just the shared-contract change, land it, then rebase your task PR on top.
- Placeholder files (e.g. `Assets/Scripts/<Module>/Placeholder.cs`) are replaced — not appended — when you implement a module. Delete them in the same PR as the real implementation.
- Never reformat or rename files you don't own. Editor auto-formatting that modifies unrelated files is a reject.

---

## 3. Branch naming

`task/T<id>-short-description` — lower-kebab-case.

Examples:
- `task/T2-common-records`
- `task/T5-anthropic-streaming`
- `task/T17-vrm-loader`
- `task/T1.5-univrm-spike`

One branch per task. Do not bundle multiple tasks on one branch.

---

## 4. PR template

Every PR must include:

```md
## Task
T<id>: <title from design doc Section 10>

## Summary
<1–3 bullets: what changed and why>

## Files touched outside my task's scope
<Must be empty. If not empty, each file needs a one-line justification
and you should have flagged this in a comment on the design doc PR first.>

## Verification
- [ ] `Library/` deleted and project re-opens clean in Unity 6.3 LTS with zero errors and zero warnings.
- [ ] All EditMode tests pass locally.
- [ ] No new asmdef reference that crosses the one-way dependency rule (Section 1).
- [ ] No pinned version bumped (Unity `6000.3.x`, UniVRM `v0.131.x`).

## Open questions
<Link to any OQ in design doc Section 12 you hit. Empty if none.>
```

Checked into the repo at `.github/pull_request_template.md` so GitHub pre-fills it.

---

## 5. Version pinning

Unity patch version and UniVRM version are pinned in [README.md](README.md). Do **not** bump either without a design-doc change.

- Patch-bumping a non-pinned package (e.g. UniTask 2.5.10 → 2.5.11) inside `Packages/manifest.json` is fine if you own a task that touches it. Note the bump in the PR description.
- Bumping UniVRM (including patch) or Unity is **never** an allowed task-level change. Open a new design-doc PR.

---

## 6. Conventions

- **Namespaces match asmdef `rootNamespace` exactly.** E.g. files under `Assets/Scripts/Prompt/` live in `namespace AiAvatarApp.Prompt { … }`.
- **4-space indent, LF line endings, UTF-8.** Enforced by [`.editorconfig`](.editorconfig); most IDEs respect it.
- **Asmdef flags**: `allowUnsafeCode: false`, `autoReferenced: false` (except `Common`), `defineConstraints: []`.
- **Public API surface**: anything not explicitly part of a module's public contract should be `internal`. Cross-module access goes through interfaces in `Common` (or `Providers` for provider contracts).
- **No partial implementations merged.** If you can't finish the task, open a draft PR and flag it, don't land half-wired code.

---

## 7. Where to flag blockers

- **Design question unanswered?** Add it to Section 12 (Open Questions) of the design doc as a new `OQ-N` and link that PR in your task PR description. Do not guess.
- **Unity/UniVRM compatibility issue?** If T1.5 hasn't been run yet, running T1.5 is the unblocker. If T1.5 ran and your task hits something T1.5 didn't catch, update the T1.5 result doc (`spikes/univrm-unity63/RESULT.md`) and notify the owner.
- **Asmdef graph limitation?** See rule 1 above. Don't hack around it.

---

## 8. Local pre-flight before opening a PR

```bash
# From repo root.

# 1. Nuke Library to test a cold open.
rm -rf Library/ Logs/ Temp/

# 2. Let Unity resolve packages and compile. Must exit 0 with no compile errors.
/Applications/Unity/Hub/Editor/6000.3.13f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -nographics \
  -projectPath "$PWD" \
  -logFile -

# 3. Run EditMode tests. Must exit 0.
/Applications/Unity/Hub/Editor/6000.3.13f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -runTests -testPlatform EditMode \
  -projectPath "$PWD" \
  -testResults EditMode-results.xml \
  -logFile -
```

If either fails on your branch but succeeds on `main`, it's a you-problem. If both fail on `main` too, it's a scaffold problem — ping the T1 owner.
