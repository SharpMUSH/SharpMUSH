# Help source fallback and illustrated Discord guide implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline. The user authorized implementation and independent parent review; no additional design approval is required.

**Goal:** Finish #1999 and #1997 as separate reviewable changes while preserving existing help resolution and access.

**Architecture:** Extend the shipped plus-help package after its existing topic lookup fails, reusing its record selection and listing renderer. Publish a Markdown Discord setup guide with genuine screenshots and a stable GitHub main URL, linked from the existing setup help section.

**Tech stack:** SharpMUSH softcode/package YAML, Markdown help, TUnit, Blazor portal, browser screenshots.

**Spec:** [#1999](https://github.com/SharpMUSH/SharpMUSH/issues/1999), [#1997](https://github.com/SharpMUSH/SharpMUSH/issues/1997).

## Global constraints

- Preserve all current topic lookup, unknown/search, explicit listing and compact overview behavior.
- Source fallback must use the existing reader-visible record/listing path; no additional visibility bypass.
- Never capture/reveal/generate/reset bot tokens or client secrets, mutate the Discord bot, or send external messages.
- Screenshots must show actual interfaces, with private details excluded by capture framing; never fabricate success.
- Preserve untracked `work/`; use `/temp/codex/help-source-guide` scratch and the repository SDK.
- Serialize builds with `flock --close /temp/codex/sharpmush-build.lock dotnet build ... -m:2 -nr:false`; test with `--maximum-parallel-tests 8`.
- Keep #1797 open: its existing JSON/list fixes and census are partial. The subsequent audit/family slice gets a separate plan and PR.

## Review focus

- An exact topic/source collision must show the topic, and existing fuzzy/command topic matches remain authoritative.
- Case/registered aliases use the same source selection as explicit listing, including clickable pagination.
- Empty, missing and inaccessible sources must not become a new disclosure path.
- Setup documentation must distinguish the Discord bot bridge from optional Discord account login.
- Screenshots, permissions/intents, portal field labels and successful connection evidence must agree with current interfaces; inability to capture a state is reported, never simulated.

### Task 1: Topic-first source fallback (#1999)

**Files:** `examples/packages/plus-help/package.yaml`, `examples/packages/plus-help/README.md`, `SharpMUSH.Tests.Integration/Integration/PlusHelpSourceFallbackTests.cs`, `SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharptop.md`.

**Interfaces:** Consume `FUN`GET`LOOKUP`, `FUN`GET`OF` and `INC`DO`LISTING`; retain the existing `(query, page)` named arguments to `INC`DO`SHOW`.

- [x] Add parser-driven mortal-reader regressions comparing source fallback with explicit listing; cover discord-scenes, topic collisions, uppercase/alias registrations, unknown, empty, inaccessible and paginated source links.
- [x] Build Integration and run the focused class; confirm new fallback cases fail on current package behavior while positive controls pass.
- [x] Add the smallest fallback before the existing miss assertion; share record selection/listing rather than duplicate rendering or read raw registry content.
- [x] Explain the shortcut in the package README and shipped general help topic.
- [x] Format twice, build, run PlusHelpIntegrationTests and relevant package/help regressions, then commit. Parent review, PR publication and CI remain the next handoff gates.

### Task 2: Genuine illustrated Discord setup guide (#1997)

**Files:** `docs/guides/discord-setup.md`, `docs/guides/assets/discord-setup/*.jpg`, `SharpMUSH.Plugins.Discord/help/discord-command.md`, focused documentation/help tests.

**Interfaces:** Use the published canonical URL `https://github.com/SharpMUSH/SharpMUSH/blob/main/docs/guides/discord-setup.md`; retain `@discord setup` and `@discord/connect` lookup aliases.

- [ ] Read current bot/plugin/admin contracts and official Discord docs; inspect actual Developer Portal and local SharpMUSH UI without bot mutations.
- [ ] Capture application selection, bot intents/settings, invite review/permissions, game configuration/connection, channel linking and ID-copy guidance from genuine interfaces. Keep secrets/private details outside capture bounds; caption every image and supply useful alt text.
- [ ] Write ordered setup, permission/intents, connection diagnosis and channel/member guidance matching source; distinguish OAuth social login.
- [ ] Add the canonical guide link to setup help and a regression protecting lookup aliases plus rendered browser/terminal URL behavior; verify image/link references.
- [ ] Visually inspect the guide at desktop and narrow widths, show resulting screenshots with Codex-show, then commit, parent-review and create/attach a ready own-org PR.

## Self-review

All issue acceptance cases map to the tasks above. Source fallback preserves existing visibility by sharing listing input; it does not create an independently interpreted source registry. Documentation publication is GitHub main after merge, with reviewable branch assets before merge. Parent owns independent review and final merge/CI guard. No runtime change is planned for the guide.
