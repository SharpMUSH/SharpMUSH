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
