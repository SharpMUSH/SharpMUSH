# Public softcode contract audit implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task. Approval and inline execution were supplied by the controller; no further design approval is required.

**Goal:** Reconcile the shipped public registration census and correct the confirmed JSON signatures/help with executable consumer regressions, leaving #1797 open.

**Architecture:** Reuse reflection RegistryInventory and compare generated FunctionLibrary/CommandLibrary metadata by name and contract. Export a deterministic durable census carrying per-entry review status, source origins, aliases, switches and callback/consumer evidence. Correct JSON metadata at the declaring attributes so both generated analysis catalog and portal definitions consume it.

**Tech stack:** .NET 11.0.100-rc.2, TUnit, generated metadata, ClientDataGenerator, Markdown help.

**Spec:** ../specs/2026-10-16-open-issue-consolidation-design.md, Audit section.

## Global constraints

- Keep #1797 open until full behavioral acceptance; enumeration is not behavioral review.
- No merge/deploy; one owned-repository PR after independent review.
- Do not equate parameter-name count with MaxArgs or generate docs from unverified metadata.
- Preserve binding data trunk; other behavior changes belong to their existing implementation lanes.
- Use repository-pinned SDK; builds `-m:2 -nr:false`; disk scratch `/temp/codex/audit`; set TMPDIR/TMP/TEMP.

## Review focus

- Variadic callback extras must remain visible after json_map output separator, without false arity rules.
- json_mod merge-patch argument is not a JSONPath and the optional fourth argument is JSON.
- JSON strings arrive quoted at callbacks; examples must not double-encode them.
- Nested arrays/objects/null must survive a recursive example with balanced syntax.
- Aliases/switches and newly added registrations must reconcile without frozen counts; dynamic/plugin/live surfaces remain explicitly unknown/pending.

### Task 1: Census and mechanical drift checks

**Files:** `tools/ContractAudit/{ContractAudit.csproj,Program.cs}`, `SharpMUSH.Tests.Infrastructure/RegistryInventory.cs`, `SharpMUSH.Tests/Documentation/RegistryContractAuditTests.cs`, `docs/audits/softcode-contracts.json`, `docs/audits/softcode-contracts.md`.

**Interfaces:** Consume RegistryInventory.Functions/Commands, FunctionLibrary.CreateMetadata(), CommandLibrary.CreateMetadata(), AliasOptions.Default; produce a portable JSON census with one status/evidence record per registered spelling and command switch.

- [x] Add registry parity tests comparing every name, arity, flags, parameter label, switch and alias target against generated metadata; inspect failures before adjusting checks.
- [x] Generate durable census from reflection after reconciliation, status `pending-behavioral-review` except the explicitly tested JSON signatures/examples. Store origin/evidence per entry and include callback candidate declaration/invocation links plus consumers; exclude live/custom/plugin surfaces with unknown totals.
- [x] Verify deterministic census coverage without hard-coded registration counts, permitting new sibling entries while reporting them as pending.

### Task 2: JSON consumers and examples

**Files:** `SharpMUSH.Tests/Documentation/RegistryContractAuditTests.cs`, `SharpMUSH.Tests/Functions/JsonFunctionUnitTests.cs`, `SharpMUSH.Implementation/Functions/JSONFunctions.cs`, `SharpMUSH.Documentation/Helpfiles/SharpMUSH/json-{map,mod}-function.md`.

**Interfaces:** Consume BuiltInAnalysisCatalog.CreateSnapshot() and ClientDataGenerator.Files(TestPaths.Helpfiles); expected json_map labels `attribute,json,osep,arg...`, json_mod labels `json,action,path-or-patch,json2`.

- [x] Write consumer assertions for corrected metadata in analysis catalog and portal signatures; run and record intended failures.
- [x] Run parser reproductions of raw JSON callback quoting, custom separator/extras, action-specific modification and recursive help examples before replacing help.
- [x] Correct attribute labels and help prose/examples; preserve JSON execution semantics.
- [x] Verify parser examples and consumer regressions green; run HelpRegistryParityTests, HelpExportTests, MushDefsCrossReferenceTests and full dependent test suites; report all failures/skips.

### Task 3: Evidence handoff

- [x] Record exact base, red/green commands/results and pending scope in durable audit Markdown; link sibling lane evidence without duplicating their changes.
- [x] Format changed C# twice to convergence, build/test with pinned SDK, commit and push a clean stable head for independent review.

## Execution handoff

Implemented inline by the dispatched audit worker; independent review and the single PR remain controller-owned gates. The full engine run is controller-owned combined integration validation, explicitly replacing a duplicate audit-tree full run. Exact scoped results, pending command-arity ruling and census limitations are in `docs/audits/softcode-contracts.md`.
