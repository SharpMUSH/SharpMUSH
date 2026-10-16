# Stored markup safety (#1818)

Approved [consolidation design](../specs/2026-10-16-open-issue-consolidation-design.md).

1. Pin unsafe future-wire, damaged and opaque inspection behavior in regression tests.
2. Carry serialized provenance on attribute aggregates from IAttributeStore through existing Mediator queries; add a permission-checked stored-value write that preserves original bytes via the existing invalidating command.
3. Preserve source content in copy/move/clone and snapshots; preflight every matched @edit attribute before effects. Explicit replacements and pure edit() remain ordinary writes/string operations.
4. Isolate malformed rows at enumeration/startup boundaries, retaining read accounting and diagnostics.
5. Preserve scene original content/history and refuse edits that would serialize degraded fallback.
6. Verify storage, snapshot, edit, startup, feed and scene regressions; format twice, run dependent tests, document reader-first rollout/rollback, commit.

No wire upgrade or automatic migration is introduced. Minimum-reader stamping waits for a new writer wire version.
