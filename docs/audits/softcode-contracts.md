# Public softcode contract audit (#1797)

Baseline: `38bab40c33ce362fe147b9b1d209367a02193b55` (16 October 2026), plus this lane's JSON metadata/help fixes. **#1797 remains open.** Enumeration and metadata reconciliation do not establish behavioral compatibility or a completed per-entry audit.

## Census and status

[softcode-contracts.json](softcode-contracts.json) records every declaration in the implementation assembly, its complete declared metadata, source candidate location, implementation method, status and evidence links. Separate records enumerate each shipped alias spelling, each command/switch declaration, and every EventCatalogue event callback with declared ordered arguments, enactor and privacy policy. Every public entry remains `pending-behavioral-review`: the two JSON entries have focused signature/example evidence, not a full authority, permissions, errors, limits and compatibility review.

The exporter compiles the existing [RegistryInventory](../../SharpMUSH.Tests.Infrastructure/RegistryInventory.cs) directly, rather than maintaining another reflection walker. It reconciles all reflected names and every public contract property with `FunctionLibrary.CreateMetadata()` and `CommandLibrary.CreateMetadata()` before writing anything. Source text supplies navigation and callback **candidates**, never membership of the registration census. `GeneratedRuntimeRegistrationsMatchMetadata` separately compares actual generated `Create(instance)` registrations with metadata using the real server fixture; no delegate evaluation or runtime-null stand-in is used for that check.

At this baseline: 661 function declarations, 223 command declarations, 28 function alias spellings, 11 command alias spellings 643 command/switch declarations and 52 declared event callbacks. Counts are observations, never test constants. The live reconciliation compares sets/contracts rather than parameter count or fixed totals. An alias that overlaps a declared name remains represented as an alias declaration; consumers may suppress duplicate spelling entries. `SingleArgumentSwitches` stays separate from the list of public switches.

Dynamic configured aliases, game-defined functions/commands, third-party loaded plugin modules, dynamic HTTP/package callback registrations, and live analysis identity/visibility are explicitly `unknown-pending-inventory`, with null totals. Their absence from the shipped implementation census is not evidence of zero entries or reviewed safety.

Regenerate from the repository root using the pinned SDK, with disk-backed TMPDIR/TMP/TEMP:

```bash
dotnet build tools/ContractAudit -m:2 -nr:false
dotnet run --no-build --project tools/ContractAudit -- . docs/audits/softcode-contracts.json
```

Regenerate after merging sibling lanes or changing registration metadata. Review the resulting diff and rerun `RegistryContractAuditTests`. New declarations become pending automatically; completed review must be recorded separately rather than inferred from a new count.

## Function consumers

- Runtime: [Functions.cs](../../SharpMUSH.Implementation/Functions/Functions.cs) calls generated `FunctionLibrary.Create(this)`, adds restricted-operation identity, then expands configured aliases. [AliasOptions.cs](../../SharpMUSH.Configuration/Options/AliasOptions.cs) supplies shipped defaults; live configuration may differ.
- Analysis: [BuiltInAnalysisLibraries.cs](../../SharpMUSH.LanguageServer/Services/BuiltInAnalysisLibraries.cs) uses generated metadata plus shipped aliases. [BuiltInAnalysisCatalog.cs](../../SharpMUSH.CodeAnalysis/Catalog/BuiltInAnalysisCatalog.cs) exports immutable snapshots; [AnalysisCatalogAnalyzer.cs](../../SharpMUSH.CodeAnalysis/Catalog/AnalysisCatalogAnalyzer.cs) rehydrates them. [SignatureHelpHandler.cs](../../SharpMUSH.LanguageServer/Handlers/SignatureHelpHandler.cs) returns analyzer signatures. [InlayHintHandler.cs](../../SharpMUSH.LanguageServer/Handlers/InlayHintHandler.cs) expands parameter labels independently. Hover and completion also consume analyzer metadata. Focused tests exercise the snapshot, analyzer signature, generated portal definitions and actual registered Monaco signature provider in Node; live LSP identity, inlay rendering and browser interaction remain pending.
- Portal: [ClientDataGenerator.cs](../../tools/ClientData/ClientDataGenerator.cs) joins reflected metadata and indexed shipped help, expands aliases, and writes `mush-defs.json`. The client build invokes it. [mush-monaco.js](../../SharpMUSH.Client/wwwroot/js/mush-monaco.js) reads the result for completion/hover/signatures; [HelpService.cs](../../SharpMUSH.Client/Services/HelpService.cs) uses the same definitions for the drawer. Flat fallback lists contain names, not authoritative signatures. Game help uses its own `GameHelpService` boundary.
- Documentation: [HelpExport.cs](../../SharpMUSH.Documentation/HelpExport.cs), `HelpSignature`, `HelpRegistryParityTests`, `HelpExportTests`, and `MushDefsCrossReferenceTests` check export/readability/arity/references. Those checks do not prove argument meaning, callback authority or execution order.

## Command consumers

[Commands.cs](../../SharpMUSH.Implementation/Commands/Commands.cs) calls `CommandLibrary.Create(this)` and adds configured aliases. The analysis and portal consumers above export command labels, flags and switches from the same declarations. Reflection/generated comparison includes single-argument switches, locks, output policy and argument labels; no assumption that labels count equals maximum arity is made.

**P2 pending discrepancy:** nine legacy commands declare `MaxArgs=0` below `MinArgs`: `@EDIT`, `@MESSAGE`, `@NOTIFY`, `@PASSWORD`, `@SCAN`, `@SKIP`, `@VERB`, `GET`, `GIVE`. The dispatcher deliberately does not generally enforce command arity (see `RejectIfTooFewArguments` in Commands.cs), and many additional zero-maximum declarations advertise real arguments. Do not mechanically rewrite zeros or reject these registrations with a function-style bound check: inspect each implementation and each editor consumer's treatment first. This remains visible in per-entry metadata and is not claimed fixed by the JSON lane.

## Callback entry points and remaining review

Event callback entries come directly from `EventCatalogue.All`, not a source pattern; their declared schemas are inventoried while invocation behavior remains pending. The JSON `callbackCandidates` array records source invocation/declaration candidates under Implementation, Library and Server with source locations and `pending-contract-review` status. It is a navigation aid with possible false positives, not a count of distinct public callback contracts. Each registered producer remains in the primary census even if no textual candidate matched. Dynamic registrations are unknown as above. For each producer, a behavioral review must pin supplied values/keys, named and numbered frames, authority, evaluation count/laziness, error propagation, output capture and admission timing.

| Surface | Owning entry points and consumers | Status / existing evidence |
| --- | --- | --- |
| Attribute callbacks and lambdas | [AttributeService](../../SharpMUSH.Library/Services/AttributeService.cs), AttributeFunctions, U2 variants, list map/filter/fold/chain/loop families, JSONFunctions, InformationFunctions | Pending per public producer. #1788–#1791 argument lane owns named/numbered/presence/local-signature changes; do not duplicate them here. |
| Guided input exit/timeout | [InputSession](../../SharpMUSH.Library/Models/InputSessions/InputSession.cs), InputSessionService, InputSessionCommands, TaskScheduler | Pending lifecycle/actor/admission audit; [guided-input-ordering](../design/guided-input-ordering.md) is the existing contract. |
| Trigger/include/wait/semaphore/loop/event | QueueCommands, ControlFlowCommands, TaskScheduler, [EventCatalogue](../../SharpMUSH.Library/Definitions/EventCatalogue.cs) | Pending per producer. #1792/#1793/#1741 queue lane owns inline/admitted behavior, snapshots, budgets and accounting. |
| HTTP and portal commands | [PortalCommandService](../../SharpMUSH.Server/Services/PortalCommandService.cs), CommandsController, HTTP route/handler implementations | Pending route registration plus trusted-actor/capture/result contract review. Runtime configured routes are unknown; a function signature is insufficient. |
| Package/application callbacks | Package installation lifecycle, application source registry, widget/application actions | Pending; plugin/dynamic totals unknown. #1794–#1796 application lane owns shared state and Jobs proof. |

Sibling issue links are ownership/evidence entry points, not claims their tests have run at this baseline: [arguments #1788](https://github.com/SharpMUSH/SharpMUSH/issues/1788), [queue #1792](https://github.com/SharpMUSH/SharpMUSH/issues/1792), [applications #1794](https://github.com/SharpMUSH/SharpMUSH/issues/1794). Their PR evidence must be reviewed at their actual heads and the merged registration census regenerated.

## JSON contracts

**P1 fixed after reproduction:** `json_map` argument 2 is `osep`, followed by callback extras starting at `%3`, rather than a JSONPath. `json_mod` argument 1 is `action`, argument 2 is an action-dependent path or merge patch, and argument 3 is optional replacement JSON. Corrected declaration labels flow to generated analysis catalog and portal signatures. These are metadata/help corrections; parser execution semantics are unchanged.

`json_map` supplies `%1` as raw JSON: strings retain quotes, arrays/objects retain structure and null is literal `null`. The primitive examples now show those quotes. The recursive help example selects on `%0`'s type and passes primitive `%1` through rather than re-encoding already encoded strings. The example's invocation has its missing closing parenthesis restored. Its stored attribute bodies are read directly from the shipped help and executed by the real parser test, so changing the documentation can break the regression.

### Validation evidence

Validation results are recorded here before handoff. Commands use `dotnet run --no-build --project SharpMUSH.Tests -- --treenode-filter '/*/*/<class>/<method-or-*>'`; filters run separately, because pipe expressions unexpectedly select every test.

- RED: `JsonSignaturesReachAnalysisAndPortal` failed for both expected contracts; actual labels were `attribute,json,path` and `json,path,value`.
- Raw parser baseline: `JsonHelpContractExamples` passed 5/5, demonstrating quoted strings, separator/extras forwarding, merge patch and path/value calls before any metadata/help fix.

- RED, immutable baseline help: the example fixture found no stored first printer definition (`get(.../pretty_json)` returned empty); executing the corrected shipped definitions succeeds. The repaired invocation and primitive handling are covered by `JsonMapNestedHelpExamplePreservesRawValues`.
- GREEN: `dotnet build SharpMUSH.Tests -m:2 -nr:false` and `dotnet build tools/ContractAudit -m:2 -nr:false`, zero warnings/errors, pinned SDK 11.0.100-rc.2.26504.105.
- GREEN, separate class filters: `RegistryContractAuditTests` 4/4; `JsonFunctionUnitTests` 123 passed, 2 existing OOB setup skips; `HelpRegistryParityTests` 6/6; `HelpExportTests` 1/1; `MushDefsCrossReferenceTests` 2/2; `MushCodeAnalyzerIntelligenceTests` 12/12; `RegistryCoverageInventoryTests` 1/1.
- GREEN: exporter run twice, byte-identical outputs (`cmp` exit 0); all 884 primary declarations have source navigation; 52 event schemas and 247 callback source candidates are explicitly pending. Local Markdown evidence links resolve.
- Formatting: changed C# formatted twice to convergence; final build ran its formatting gate; `git diff --check` passed.
- Full engine suite is reserved for the controller's combined six-lane integration run, as explicitly directed, avoiding a duplicate expensive feed/storage run. No browser session, live LSP identity check, live game audit, merge or deployment was performed. Independent whole-branch review remains the controller's next gate.

### Rulings and scope

The initial attempt to apply function-style `MinArgs <= MaxArgs` to commands failed on the nine documented zero-maximum entries. Inspection of the existing per-command arity opt-in showed that a blanket command correction would change unrelated public contracts; the mechanical gate instead checks nonnegative command bounds and exact reflection/generated equality, retaining the discrepant metadata for behavioral review. This is a deliberate pending review item, not an approved new interpretation of zero.

Aliases and portal definitions preserve their declared key spelling; the mechanical test compares that spelling and the exact ordered parameter array, rather than assuming every key is lower-case. It does not claim browser command-alias lookup behavior is audited. Newly merged functions/commands remain compatible with these tests because they enumerate the current registries; the checked-in baseline census is not a frozen-count CI gate.

### CI and review follow-up, 16 October 2026

PR1965 job117109161555 at `5b90ab1fbacfd7b1152009f963ef69dfb1e95353` failed only `EveryHelpEntryFitsOnATelnetScreen`: the nested printer entry rendered 42 rows against the 30-row limit. The full worked invocation stays with its definitions; a declared `json_map printer output` section holds the unchanged output and is linked from the definition. `JSON_MAP3` still resolves to the definition/call section.

Review thread `PRRT_kwDOLef2xs6tT2mj` identified a separate portal consumer defect: declaration pattern `arg...` reached Monaco literally, followed by absolute-position names `arg5`, `arg6`. The registered-provider regression in `tools/client-tests/monaco-signatures.test.mjs` failed 3/3 for one, two and four callback extras before the fix. Monaco now expands single and paired variadic patterns with the same numbering as the analyzer. Tests invoke the actual provider returned by Monaco registration and check displayed labels and active argument; paired patterns and fixed `json_mod` labels are positive controls. The Node fixture supplies declaration metadata because its standalone CI job has no .NET build; the C# registry test independently verifies that generated portal data has those labels.

Review thread `PRRT_kwDOLef2xs6tT6Sz` is a false positive. `GeneratedRegistrationsMatchReflection` compares `FunctionLibrary.CreateMetadata()` / `CommandLibrary.CreateMetadata()` (new attributes emitted by the Roslyn source generators) with `RegistryInventory` (attributes read through `MethodInfo.GetCustomAttributes` from implementation methods). These are independent sources, not identical values by construction. The comparison remains intact.

Follow-up validation uses the pinned SDK on PATH, `TMPDIR=TMP=TEMP=/temp/codex/audit`, Podman socket, `-m:2 -nr:false`, and individual TUnit class filters:

- `node --test tools/client-tests/*.test.mjs`: 90/90, including five real-provider signature tests.
- `dotnet build SharpMUSH.Tests -m:2 -nr:false`: zero warnings/errors. An initial invocation used the pinned executable but an older SDK on PATH for its child formatter; the corrected invocation also verifies formatting.
- `HelpHeightTests`: 1/1; `HelpNavigationTests`: 4/4; `RegistryContractAuditTests`: 4/4.
- `JsonMapNestedHelpExamplePreservesRawValues`: 1/1 against the actual shipped definitions; nested arrays, object strings and null remain intact.
- `git diff --check`: passed. No C# source changed in this follow-up.

## List transformation signatures

Focused follow-up at `14fd9ab7aaf5baaaf13e2d2bcefe9641a55d0e4b` plus these metadata corrections. The regenerated census keeps every entry `pending-behavioral-review`; these three entries now link the focused consumer/parser evidence below. This does not complete their permission, error, resource-limit or callback-identity review, and #1797 remains open. The original census counts above describe the original baseline; this regeneration reconciles 663 functions, 224 commands, 644 switches, 28 function aliases, 11 command aliases, 52 event schemas and 248 callback candidates without frozen test totals.

| Function | Observed incorrect Monaco signature before correction | Correct ordered labels |
| --- | --- | --- |
| `sortkey` | `sortkey(list, attribute, [delimiter], [arg4], [arg5])` | `attribute, list, sort-type, delimiter, osep` |
| `index` | `index(list, element, delimiter, arg4)` | `list, delimiter, first, length` |
| `munge` | `munge(attribute, list1, list2, [list3], [delimiter])` | `attribute, list1, list2, delimiter, osep` |

The actual implementation reads these corrected argument positions; the shipped `sharpfunc.md` entries for `index` and `sortkey`, and `munge-function.md`, already document that order. Help therefore needs no rewrite. Only registration labels change. Evaluation, arity, callback arguments and runtime behavior are preserved. The set-function overloads are outside this correction slice.

`ListSignaturesDescribeActualCallShapes` checks the analysis catalog through its real analyzer and generated portal definitions. The actual registered Monaco provider is exercised with the regenerated census metadata in `monaco-signatures.test.mjs`; this avoids a second hand-maintained list-signature fixture. Literal expected labels and active parameter positions protect the author-visible result. `ListFunctionUnitTests` covers full calls with input and output separators, including a multi-character `index` delimiter; the `munge` callback reads its supplied delimiter as `sort`'s third argument.

Validation uses SDK `11.0.100-rc.2.26504.105`, `-m:2 -nr:false`, `TMPDIR=TMP=TEMP=/temp/codex/list-contract-metadata`, and rootless Podman for parser fixtures:

- RED: the three new analyzer signature cases fail with the old labels; the three new actual Monaco provider cases fail with the same wrong call shapes. Five existing provider cases pass.
- Parser characterization before the metadata correction: `ListFunctionUnitTests` passed 407/407, including all three new full call-shape cases.
- GREEN: `dotnet build tools/ContractAudit -m:2 -nr:false` passed with zero warnings/errors; two exporter runs were byte-identical (`cmp` exit 0).
- GREEN: complete client suite `node --test tools/client-tests/*.test.mjs` passed 93/93, including the three new actual-provider regressions.
- GREEN: `dotnet build SharpMUSH.Tests -m:2 -nr:false` passed with zero warnings/errors; `RegistryContractAuditTests` passed 7/7 against the corrected generated catalog and portal definitions.
- GREEN: corrected `ListFunctionUnitTests` passed 407/407; changed C# formatted twice to convergence and `git diff --check` passed.
- Full engine suite, live LSP/browser interaction and production validation were not run for this metadata slice. CI and independent review remain required before merge; no merge or deployment was performed.

## Set-operation overloads

The four set operations (`setunion`, `setdiff`, `setinter`, `setsymdiff`) now declare all five canonical labels: `list1, list2, delimiter, sort-type, osep`. Their arity and execution are unchanged. Twenty-four real parser cases characterize the existing canonical and TinyMUX-reversed five-argument calls, four-argument sorting, an empty fourth argument, an invalid sort token, and an empty input delimiter. In particular, an unknown non-empty fourth argument is a sort token, not a non-empty output separator. The shipped help now describes that behavior; the reverse five-argument spelling remains documented.

The actual analyzer, generated portal definitions and registered Monaco signature provider each cover these five labels and the active fifth parameter. These are focused argument/consumer checks; permission, resource-limit, complete compatibility and live consumer identity review remain pending. #1797 remains open.

## Shipped plugin inventory

Schema 2 also inventories the actual generated registrations of every plugin referenced by the Server project: Scene, Discord, Lsp and Mcp. Each module's reflected declarations and complete metadata must agree with its generated `GetFunctions()` / `GetCommands()` results. Module-qualified rows keep namespaces distinct. Empty modules have explicit zero-registration records. The exporter fails if the Server's shipped project references change without an inventory update.

The census records a SHA-256 fingerprint of the sorted tracked file paths and their bytes, excluding the census itself. This immutable content identity stays reproducible across clean regeneration and the commit that contains it; Git HEAD and dirty state would be self-referential here. Per-entry reviewed facets distinguish registration evidence and focused parser/signature evidence from pending semantic review; no entry is marked behaviorally complete. Callback source candidates now include shipped module sources but remain candidates. Dynamically loaded third-party modules, configured aliases, game-defined contracts and live identity/visibility still have unknown totals. Historical baseline counts above remain historical observations.

Validation of this slice: actual pre-change parser characterization 24/24; missing-label regressions RED 4/7 analyzer cases and four Monaco cases; corrected generated analysis/portal contracts 11/11; complete list-function family 431/431; shipped-plugin census regression RED against schema 1 then GREEN 1/1 against schema 2; complete client Node suite 99/99. Tests and exporter build with zero warnings/errors. Two exports compare byte-for-byte. Corrected help initially exceeded the screen limit by two rows; concise wording passes `HelpHeightTests` 1/1 without changing the gate. Full engine CI and independent review remain required before merge.

PR review found that Git HEAD/dirty provenance changed merely because the exporter wrote its own tracked output. Actual clean-checkout reproduction produced different first and second exports. Provenance now identifies immutable tracked content (sorted UTF-8 paths and file bytes, each length-prefixed) and excludes the census. A regression verifies that census rewrites and input ordering preserve identity while source edits change it. This avoids claiming that a generated artifact can contain its own future commit hash.


## UNIQUE comparison labels

The adjacent `unique` declaration also mislabeled its second and third arguments and omitted the fourth: its actual positional contract is `list, sort-type, delimiter, osep`. Unlike a set union, it removes only consecutive duplicates and does not reorder the list. The existing shipped help already describes this accurately and is retained. Two actual-parser cases characterize numeric comparison and preservation of a later nonconsecutive duplicate before the declaration correction. Analyzer/generated portal and registered Monaco regressions verify the corrected ordered labels and active fourth parameter.

The positional function bodies and arity are unchanged. Built-in calls currently receive numbered argument frames in `FunctionInvocationPipeline`; that path does not read `ParameterNames`. Named attribute bindings use the separately registered `@function/args` argument names. Correcting these built-in declaration labels is not a new named-call syntax or a claim that those separate binding contracts were audited here.

## JSON query actions and fill pairs

The approved next metadata slice covers only `json_query` and `json_fill`. The inventory at `cb4b3321d21112c19b29ab542a8bdf17cd104f7a` found that `json_query` called its second argument `path`, although the implementation and shipped help identify it as an optional action (default `type`). `get` and `exists` consume following path segments; `extract` consumes one combined path. The declaration now uses `json, action, arg...` without imposing a new action-dependent arity rule.

`json_fill` consumes pointer/value pairs after its template. Its old declaration, `json, pointer, value...`, made the registered Monaco provider label a second pointer as `value2`. The existing paired pattern now declares `json, pointer...|value...`, expanding to `pointer1, value1, pointer2, value2`. Both shipped help entries already describe the correct call shapes and are retained. Function bodies, arity, flags, evaluation and error behavior are unchanged.

The focused regressions exercise the actual analysis catalog/analyzer, generated portal definitions and registered Monaco signature provider. Existing JSON parser tests characterize default/action queries, segmented and combined paths, multiple fills and rejection of incomplete pairs. These are partial contract checks: full error/permission/resource-limit/compatibility review and live browser/LSP identity remain pending. The census retains `pending-behavioral-review` and #1797 remains open.

One separate source-only candidate remains under this tracker: `json_mod(json(object,a,1),patch,null)` appears to pass a null `JsonNode` to `ApplyMergePatch`, whose non-object branch calls `DeepClone()`. Existing patch fixtures cover null object members but not a whole-document null patch. This candidate has not been reproduced through the parser in this slice; its caller-visible error and compatibility contract need reproduction and prioritization before any behavior change. No `json_mod` implementation changes are included.

Validation on merged paging baseline `79f3c824c96af7ea121f14b45eb44f2c7f6e5e9d`, with SDK `11.0.100-rc.2.26504.105`, disk-backed scratch and maximum test parallelism 8:

- RED: six focused C# cases fail with old metadata (five at actual analyzer labels, the query with omitted action at the generated portal declaration); registered Monaco provider cases fail 5/19 while 14 controls pass.
- Parser characterization before correction: `JsonFunctionUnitTests` 123 passed, two existing OOB setup skips. After correction: the same 123 passed/two skips; bodies, arity and evaluation are unchanged.
- GREEN: normal tests and exporter builds have zero warnings/errors; all `RegistryContractAuditTests` pass 19/19, including the independently merged paging contracts.
- GREEN: the registered Monaco signature suite passes 19/19 against a real exporter-produced scratch census candidate. Two exports compare byte-for-byte; the original tracked census was restored after this fixture check.
- Changed C# files were formatted twice and `git diff --check` passed. No broad engine run or live browser/LSP test was performed for this slice.

The exporter hashes the sorted tracked paths and their on-disk bytes, excluding the census itself; it does not use Git HEAD as its content identity. Scratch candidates are working-source review evidence. Final tracked census generation is reserved for the approved source commit, followed by a census-only commit, so the snapshot includes the merged paging registration without claiming behavioral completion.
