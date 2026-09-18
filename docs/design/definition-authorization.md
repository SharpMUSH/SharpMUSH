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
inside one writer transaction; a refused creation returns the existing nullable failure result.
Existing mixed-case records keep their IDs and assignment edges; lookup resolves their names
case-insensitively. Definition update operations do not rename primary names, and seed refresh
completes during startup before game commands run.

Seed refresh retains its separate upsert paths. Command prechecks provide duplicate diagnostics,
while the provider boundary supplies the atomic no-replacement guarantee. Existing Mediator cache
invalidation remains in force, including conservative invalidation after a refused create. Symbols
may be shared subject to existing type rules; global alias uniqueness is a separate registry policy.

Object flag assignment is separate from definition management. The selected set or unset array
treats DARK, MDARK, ODARK, LOG and EVENT as visibility/effect metadata, not privilege requirements.
INTERNAL and DISABLED prohibit that operation before any principal alternative is considered;
a disabled definition prohibits both operations even with empty permission arrays. Remaining
built-in privilege levels and custom flag/power names retain SharpMUSH's OR semantics. An array
containing only metadata adds no privilege requirement. The full Controls authorization gate,
including eligible zone and control locks, and the type- and flag-specific checks still apply.
SYSTEM protects definition management and does not itself prohibit assignment.
Definitions retain their stored metadata and remain queryable for administration; no migration or
global disabled-definition filtering is required.
