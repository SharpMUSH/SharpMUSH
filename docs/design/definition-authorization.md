# Flag and power definition authorization

FLAG and POWER select one operation before authorizing it. The selected operation also controls
dispatch, so an unused read or debug switch cannot lower a mutation's required privilege.
Definition mutations require God; LIST and DECOMPILE remain open, and selected FLAG DEBUG requires
Wizard. The no-switch forms retain their existing display and power-assignment authorization.

Operation precedence is preserved:

- FLAG: LIST, ADD, DELETE, LETTER, TYPE, ALIAS, RESTRICT, DECOMPILE, DISABLE, ENABLE, DEBUG.
- POWER: LIST, ADD, DELETE, ALIAS, LETTER, TYPE, RESTRICT, DECOMPILE, DISABLE, ENABLE.

Thus LIST+ADD selects a read, DECOMPILE+DISABLE selects a read, and ADD+DECOMPILE selects a God-only
mutation. DISABLE wins when both enable/disable switches occur. System-definition restrictions
still apply after authorization.

Provider creation refuses an existing primary name case-insensitively. Lightning checks and inserts
inside one writer transaction. Surreal uses one conditional statement with a case-insensitive logical
name check and a native CREATE at the canonical uppercase record ID. Explicit RETURN supplies the
actual created record; an empty or failed creation returns the existing nullable failure result.
Concurrent new creates contend on that same ID. Existing mixed-case records keep their IDs and
assignment edges; lookup resolves their names case-insensitively. Definition update operations do
not rename primary names, and seed refresh completes during startup before game commands run.

Seed refresh retains its separate upsert paths. Command prechecks provide duplicate diagnostics,
while the provider boundary supplies the atomic no-replacement guarantee. Existing Mediator cache
invalidation remains in force, including conservative invalidation after a refused create. Symbols
may be shared subject to existing type rules; global alias uniqueness is a separate registry policy.
