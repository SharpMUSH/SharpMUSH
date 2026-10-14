# Live analysis catalog

`IAnalysisCatalog.GetSnapshotAsync` receives a host-resolved executor, not client principal fields.
The host registers `LiveAnalysisCatalog` against the live parser libraries, user-function registry
and existing lock service. This slice adds no discovery endpoint or MCP tool.

Capture takes one shared mutation gate across function/command maps and the built-in user-function
registry. Mutable metadata and collections are copied while holding it. The gate is released before
permission reads/awaits. Ordinary dispatch lookup/enumeration remains lock-free. Map writes and
`LibraryService.MutateMetadata` serialize with capture; metadata edits advance `Version` even when
an edit throws after partially changing a value. Snapshot consumers must detach values inside the
`Capture` projection. Do not retain mutable attributes from the projection.

Core command argument-name/restriction/configured-baseline edits now use `MutateMetadata`. They keep
the same attribute instances, preserving alias and parked-command identity. Plugins that edit
metadata must use this API; arbitrary direct writes to public attribute objects cannot participate
in the consistency guarantee. Third-party user-function registries must implement `CaptureCatalog`;
the default implementation fails closed rather than pretending `All()` is a consistent snapshot.

Visibility reuses `FunctionDispatcher.CheckPermissionAsync`, `CommandRestrictions.PermitsAsync`,
actual command lock evaluation and function restriction overlays. Each compiled alias is filtered
by its invoked-name overlay, matching runtime behavior. Global and exactly the executor's full
owner-local scope are considered; other local scopes and recycled object identities are excluded.
Disabled/unresolvable user functions are omitted. Compiled names retain precedence. Owner-local
entries have `Origin=owner-local`: callers must not suggest them as ordinary global invocation.

Compiled functions and commands use `Origin=compiled`, since the current runtime table does not
distinguish core from plugin provenance. No evaluation delegate or backing attribute value escapes.
The metadata revision is content-addressed over principal identity, visible entries and relevant
alias overlays, with a catalog-instance epoch invalidating restart/world replacement. No cache is
shared across principals.

This is a consistent metadata capture followed by permission checks, not a transaction over live
world permissions. Grants/locks may change after checks; execution still applies current runtime
permissions. Cancellation is linked into the existing ExecutionBudget and checked around capture
and permission processing. HTTP body limits and analysis scheduling/deadlines are separate pending
work; this catalog does not advertise those guarantees.
