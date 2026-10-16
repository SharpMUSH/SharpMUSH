# Argument contracts implementation

Binding [approved design](../specs/2026-10-16-open-issue-consolidation-design.md).

1. Reproduce dual binding and filterq side-input failures in evaluation tests; implement ordered positional aliases with validated numeric collisions and preserve side inputs.
2. Add immutable invocation supplied-key metadata to EvaluationContext, forwarded through ParserState and retained by queue snapshots. Attribute evaluator boundaries replace it; includes capture only overlay keys. Implement hasarg visible/supplied without evaluating values; protect nested and built-in frame behavior.
3. Reuse ArgHelpers.ArgumentNames/AddNamedArguments for owner-local ARGS registration and invocation; preserve registry ownership, lifetime and authority. Cover fixed/rest/grouped signatures and rejected definitions.
4. Exercise step aliases and short chunks, include overlays/chains, trigger bindings, signature-generated metadata, deferred values and queued metadata. Update help and migration notes. Run focused engine suites, formatting twice, then broader dependent validation.

Interface: EvaluationContext.SuppliedArgumentKeys has type `ImmutableHashSet<string>?`, with `StringComparer.OrdinalIgnoreCase`; null means no attribute invocation. ParserState forwards it. SnapshotForQueuedAction retains it through its Context copy. Visible keys remain EnvironmentRegisters; generated counts are excluded from supplied metadata.

## Acceptance matrix

- NamedArgumentTests: ordered/repeated names, normalized numeric collisions, empty and zero values, missing keys, positions above nine, eager evaluation once, invalid binding callback isolation, presence scopes and nested ownership.
- NamedArgumentCommandTests: trigger/inline and match, include/chained overlay, step real/anonymous short chunks, global fixed/rest/group/pairs signatures and invalid definitions.
- LocalFunctionTests and existing registry tests: scoped signature binding, aliases/disable/preserve/delete/ownership/restart isolation; global validator reused.
- ParserStateContextTests: immutable queued supplied metadata and deferred-value safety.
- FilterQFunctionTests plus existing list callback tests: anonymous/attribute parity, ordered side inputs, empty values, delimiters and reject q-registers.
- Existing event/feed/format/HTTP suites protect established numbered mappings; no positional mapping changes in those providers. Presence metadata is captured at their attribute boundaries.

Ruling: validation never evaluates or compares values or runs a refused callback. Existing Regular/RSArgs entry points evaluate input expressions eagerly before binding; that timing remains unchanged.

Ruling: signature updates through an alias update its concrete target in the same scope. The prior global setter silently stored unused metadata on the alias. A failing scoped-registry regression demonstrated the same defect; the shared setter now follows the existing single alias hop without namespace fallback.

Allocation audit: each attribute invocation captures one immutable case-insensitive key set in O(bindings). Presence queries perform key lookup without copying or forcing CallState values. Built-ins and queued snapshots share the immutable set; includes capture only their overlay keys. Signature-generated-key tracking is allocated only for global/local signature binding.

Ruling: positive /match acceptance exposed an existing broken matcher. TriggerAsync now uses CommandAttributeScanner.Compile / ListenAttributeSearch.Compile and PatternArguments.Capture, strips the command/listen prefix before running actions, and excludes the match string itself from numbered arguments. With /match/args, supplied aliases overlay captures while untouched captures remain. Admission remains inline for the queue lane to change; retain the action body, capture environment, and immutable supplied set at its queue boundary.

Validation also reproduces and protects #apply padding: fabricated visible slots are excluded from supplied keys by intersecting the actual input set with the callback's visible numbered slots. Queued presence is exercised by resuming a real hasarg query from SnapshotForQueuedAction after the parent argument dictionary is cleared.

## Independent review round 1: callback provenance

Two runtime regressions confirmed the review: real list callbacks omitted explicit side inputs from their Arguments dictionary, and direct callbacks kept null or unrelated parent supplied keys. `ParserState.ForAttributeArguments(Dictionary<string, CallState>)` now binds Arguments, EnvironmentRegisters and an immutable supplied-key snapshot together. Generated signature keys remain excluded. List callbacks pass their complete actual item/position/side-input dictionary to the fetched evaluator. Admission timing, values, identities and counters are unchanged.

Production environment-replacement audit:

| Boundary | Presence policy |
| --- | --- |
| AttributeEvaluator direct calls | Shared invocation binder; lambdas already capture actual keys; apply intersects actual inputs with visible slots to exclude padding. |
| ListFunctions fetched callbacks | Complete item/position/side-input Arguments; fetched evaluator captures those actual keys. |
| StringFunctions/AttributeFunctions fetched wrappers | Complete explicit Arguments already reach the updated evaluator; the direct default-attribute path already captures its actual keys. |
| MessageHelpers pinned/unpinned formats | Pinned uses the shared binder; unpinned uses AttributeEvaluator. |
| ControlFlowCommands map/include | Both map modes use the shared binder. Includes retain their separate pre-overlay supplied set. |
| DatabaseCommands SQL / HttpCommands response | Each callback uses the shared binder, including named row/response inputs. |
| DidItService queued actions | Bind invocation first, then clone that Context for independent counters; zero-input actions get an empty set. |
| MovementCommands follower work | New ordinary commands have no attribute invocation; explicitly clear supplied metadata along with empty environment. |
| CommandInvocationPipeline / QueuedCommandMatch / Trigger | Existing explicit actual-key capture remains; immutable snapshots retain it. |
| Listen-pattern handler / ListenerRoutingService | Handler already captures actual inputs; routing's INPREFIX environment reaches AttributeEvaluator before executing attribute code. |
| EventService / FeedService / ConnectionAnnounceService / InputSessionService / HttpHandlerCommandService | Existing explicit fresh actual-key capture remains. |
| ParserState Empty/Fresh / queued snapshots | New roots have null metadata; snapshots retain the immutable set; changing the environment property alone is not an invocation boundary. |
| RestrictedFunctions / PortalCommandService | Standalone expression evaluation, not attribute invocations: fresh root keeps null metadata despite explicit expression inputs. |
| RecurringJobService | Empty-input attribute invocation already installs a fresh empty supplied set. |

Coverage: CallbackPresenceBoundaryTests exercises top-level and unrelated-parent contexts, pinned/unpinned formats, both map modes through the actual queue, and queued DidIt actions with inputs or zero inputs. FilterQFunctionTests covers real/lambda filterq/filter/filterbool/map presence parity with nonempty and explicitly empty side inputs. HttpCommandTests executes presence queries in prepared response callback states; DatabaseCommandTests executes real queued SQL row callbacks with an explicitly empty named column. ParserStateContextTests protects generated-key exclusions and fresh empty invocation metadata. Focused build/test commands and final results are retained in work/scratch/verification-report.md; no unrelated broad suite is repeated.

Round-1 validation: `dotnet build SharpMUSH.Tests -m:2 -nr:false -p:SkipFormatVerification=true` passed with zero warnings/errors. After two formatting passes, `dotnet format whitespace --folder . --exclude '**/bin/**' '**/obj/**' --verify-no-changes` passed. Run each suite with `dotnet run --no-build --project SharpMUSH.Tests -- --treenode-filter '/*/*/<class>/*'`: CallbackPresenceBoundaryTests 12/12, FilterQFunctionTests 14/14, HttpCommandTests 65/65, ParserStateContextTests 6/6, NamedArgumentCommandTests 49/49, MessageFormatResultTests 16/16, DidItServiceTests 14/14. The SQL filter `/*/*/DatabaseCommandTests/SqlQueuedRowCallbacksStartTheirOwnPresence` passed 2/2. Total: 178 focused passing cases. Red runs recorded 9 direct-boundary failures, 2 list side-input failures, 2 HTTP callback failures and 2 SQL callback failures before their corresponding fixes. Generated-key exclusions, zero-input markers and admission timing remain covered.


External review follow-up: matched command/listen captures now use the same case-insensitive namespace as explicit named arguments. `MatchedExplicitArgumentsOverrideNamedRegexCapturesIgnoringCase` reproduces lowercase `target` retaining its capture after explicit `TARGET,Bob` for both `$` and `^` regexp attributes, then protects the override. The test also asserts `hasarg(0,supplied)=1` for the numbered alias of the actual explicit value, and `hasarg(1,supplied)=1` for the untouched actual regex capture. Numeric aliases of actual inputs are supplied by the approved contract; excluding them would incorrectly conflate aliases with generated padding/count metadata. Generated padding/count exclusions remain covered separately.


Compatibility migration found by PR1961 CI: the Time package's HTTP reply include depended on inherited `%0` surviving `/args`. The approved numbered aliases correctly replaced it with the first explicit value, losing the JSON body. Time 1.3.1 explicitly forwards `body,%0` at callback execution and reads `%<body>`; the producer escapes `%0` while writing the deferred LOOKUP attribute. `GeneratedHttpCallbackPreservesTheDeferredResponseBody` runs the installed producer and actual HTTP scheduler admission with a held synthetic transport response, verifies no timezone before the reply, then verifies the timezone/notice and callback cleanup. Both it and the original `TheAddressIsUsedOnlyWhenTheGameTurnsItOn` failed before migration and pass afterward. This was a deterministic compatibility defect, not a CI timing issue.

Review follow-up verification: `dotnet build SharpMUSH.Tests -m:2 -nr:false` and `dotnet build SharpMUSH.Tests.Integration -m:2 -nr:false` each passed with zero warnings/errors. Exact class filters using `dotnet run --no-build --project <project> -- --treenode-filter '/*/*/<class>/*'`: NamedArgumentCommandTests 51 passed; PcreGroupNumberingTests 14 passed; UserDefinedCommandsTests 31 passed/1 pre-existing skipped cache investigation; TimeIntegrationTests 10 passed. New comparer cases were 2/2 red before the one-line comparer change. The original CI failure and actual generated HTTP callback were each independently reproduced red before the package migration.

CI package-catalog follow-up: Time manifest 1.3.1 initially retained index version 1.3.0. The whole `ExamplePackageTests` class reproduced exactly one stale-version failure (11 passed/1 failed), then passed 12/12 after updating the index. `dotnet build SharpMUSH.Tests -m:2 -nr:false` passed with zero warnings/errors before the data-only correction. Exact filter: `/*/*/ExamplePackageTests/*`. Local logs: `work/queue/catalog-build.log`, `catalog-red.log`, `catalog-green.log`.

CI scene approval capture follow-up: `ApprovalBoundary_FullMatrix` expected an empty `scenelist(mine)` answer but captured the unrelated localization key `TriggerTriggeredFormat`. Its helper enumerated every recipient's calls on the session-wide notify substitute without a queue barrier. A deterministic regression sends `@pemit/silent <other-character>=<unique-marker>` as God, proves delivery to that other character, and expects no answer to God; the original helper failed by returning the marker (147 ms). The helper now reads the existing thread-safe per-character notification recorder and brackets command capture with queue barriers. Localized permission refusals are checked against the translated resource text. The shared Scene Logger returns home in `finally`, including after a failed assertion. No scene authorization or membership assertion was relaxed. `dotnet build SharpMUSH.Tests.Integration --no-restore -m:2 -nr:false` passed with zero warnings/errors; `dotnet run --no-build --project SharpMUSH.Tests.Integration -- --treenode-filter '/*/*/SceneApprovalIntegrationTests/*'` passed all 6 cases (47.825 seconds), including the full approval matrix and new capture regression. Logs: `work/issue-consolidation/scene-capture-red.log`, `scene-capture-green-build.log`, `scene-capture-green.log`.

Final CI exercised the capture regression after another test enabled God's DEBUG flag, producing a legitimate caller command echo. The regression now creates its own caller as well as its own recipient, preserving the exact empty-output assertion without depending on shared God flags. The actual approval matrix passed in that CI run.
