# Configuration option search implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task.

**Goal:** Search authorized core/application/plugin option metadata and navigate directly to its control.
**Architecture:** A metadata-only catalog projects generated core definitions and optional per-tile field definitions. Shared descriptors also supply app UI labels and focus keys. Existing palette ranking/selection consumes these rows; destinations enforce permissions and focus only rendered controls.
**Tech stack:** .NET 11, ASP.NET, Blazor WASM, TUnit/bUnit.
**Spec:** Issue #2009 and the approved read-only design from this chat.

## Global constraints
- Preserve config.admin plus tile permissions; never send current/default values or secret state.
- Registered metadata is optional and version gated; plugin contributions follow existing load-once lifecycle.
- No new PR, no deployment. Existing drafts survive option-only navigation.
- Use /temp/codex/configuration-option-search for TMPDIR/TMP/TEMP and shared build lock.

## Review focus
- Denied options must never reach client metadata or stale identity responses.
- Unsafe routes/selectors and duplicate/oversized descriptors must be refused.
- Removed applications disappear on reopening search; older manifests remain readable.
- Same-page focus must preserve drafts; missing/unauthorized controls fail without exposing values.
- Handwritten Discord/sign-in pages share their field descriptors with discovery.

## Task 1: Catalog contracts and safe metadata endpoint
Files: Contracts/Models/Portal/Applications/ApplicationConfigurationTile.cs and new ConfigurationOption.cs; Contracts/API/SchemaBuilder.cs; Server/Controllers/ApplicationConfigurationController.cs ; the endpoint lives on the existing controller and uses generated aliases.
Interfaces: optional init-only Options collection on the original six-argument tile record; metadata-only search endpoint returns ConfigurationSearchEntry[] with safe route and field key.
- [x] Add failing endpoint tests for absent current/default values, denied metadata, lifecycle refresh, malformed option declarations and core coverage; run RED.
- [x] Implement strict safe metadata projection/authorization, deriving core canonical keys from generated metadata and route mapping shared with UI.
- [x] Run focused controller/schema tests GREEN.

## Task 2: Persisted application declarations and shared UI fields
Files: PackageManifestService.cs, PackageManifestWriter.cs, ApplicationsController.cs, PackageInstallService.Apply.cs; DiscordPlugin.cs; shared Discord/sign-in field definitions and the owning pages.
- [x] Add failing package roundtrip/version and source/UI definition tests; run RED.
- [x] Add optional format-gated option declarations, shared definition consumers and field focus keys.
- [x] Run application/manifest/Discord tests GREEN.

## Task 3: Search and navigation
Files: CommandPalette.razor; PaletteRow.cs/PaletteRanker.cs; ConfigFieldRow.razor; DynamicConfig.razor and custom config pages; shared focus helper and existing layout JS if required.
- [x] Add failing ranking, delayed-response identity, field-focus and draft-preservation tests; run RED.
- [x] Load authorized catalog when opening, discard late responses, rank all metadata, render option rows and safely focus loaded destination fields without draft reload.
- [x] Run palette/config bUnit and JS tests GREEN.

## Task 4: Review and verify
- [x] Run formatter twice and builds under shared lock; run affected suites and full BUnit without the compile lock.
- [x] Request independent root review; address actionable findings with regression tests.
- [x] Report exact validation and any residual limitations; no PR before root review.

## Execution decisions and evidence
- Discovery is `GET api/application-configuration/options`, with no-store responses. Core projection reads generated metadata only; it never constructs default options or resolves configured options.
- Optional declarations require package format 1.12. The original six-argument tile constructor and Deconstruct remain binary-compatible. Descriptor collections compare structurally across serialization/reapply.
- Reserved `#config-option-<key>` fragments preserve generic application request identity and drafts. Custom pages and generated schema controls use the same field declarations/focus keys as discovery.
- Declared secret controls navigate to the existing Secrets page; only static field metadata is returned. Search keeps the stricter config.admin plus tile/option permission gates.
- RED evidence: initial catalog 4 failures; palette/navigation 2; JS focus 2; ABI/equality and draft fragment tests; strict metadata declarations 8 failures/22; character response and delayed opening each 1 failure.
- Independent review corrections: malformed declared string metadata now fails closed, including permission/route fields; opening stamps are captured before the first await and checked after each async source, on character/auth changes, close and disposal.
- Verification logs are in `/temp/codex/configuration-option-search/`; full BUnit 2639/2639 and full client JavaScript 102/102 passed; normal BUnit build had 54 pre-existing warnings and 0 errors. Discord configuration contract tests 3/3 passed with a 0-warning/0-error build. No PR or deployment is authorized by this plan.
