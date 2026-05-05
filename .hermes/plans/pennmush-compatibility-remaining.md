# PennMUSH Compatibility — Remaining Work

## Status
- Branch: `pennmush-compatibility`
- All existing tests pass (3365/3365, 0 failures)
- Attribute tree tests: 64 passing (matching, lattr, permissions, parent permissions)
- Command inheritance tests: 2 new tests (parent-inherited $commands, no_command tree blocking)

## Completed Sections (from testatree.t)
- Attribute tree matching (hasattr, hasattrp, get with tree paths)
- Attribute tree lattr (listing with wildcards, depth, patterns)
- Attribute tree permissions (wizard flag, mortal_dark, locked, set/read enforcement)
- Attribute tree parent permissions (inheritance, no_inherit, mortal_dark on parent, hasattr vs hasattrp)
- **lattrp** (tests 1-6) — DONE. Parent tree traversal was already implemented; tests strengthened to exact assertions.
- **command** (partial) — Parent-inherited $commands now work. Tree-level no_command blocking implemented.
  - `GetCommandAttributesQueryHandler` now walks parent chain (respecting no_inherit, tree no_command)
  - Tests: `ParentInheritedCommand_Fires`, `NoCommandOnAttribute_BlocksTreeDescendants`

## Implementation Added This Session
- `GetCommandAttributesQueryHandler.cs`: Refactored to walk parent chain for inherited $commands
  - `seenNames` HashSet ensures child overrides parent (first seen wins)
  - `noCommandPrefixes` tracks tree-level blocking (attr with no_command blocks all `attr`*` descendants)
  - `no_inherit` flag on parent attributes prevents inheritance
  - Fixed flag comparison: `"no_command"` (lowercase, matches stored format)

## Remaining testatree.t Sections

### sortorder (tests 1-38) — PARTIALLY DONE
The infrastructure is now in place (parent inheritance + tree no_command). Remaining tests exercise:
- [ ] `@set parent/abc=no_command` blocking `abc`xyz` from child (DONE in handler, needs more test coverage)
- [ ] `no_inherit` on parent attr trumping no_command on leaf (atree.sortorder.28-30)
- [ ] Child `&abc` masking parent's `$abc` command (atree.sortorder.14-15)
- [ ] `@wipe` clearing command caches (atree.sortorder.36-38)
- [ ] Full "say" output capture tests matching exact oracle patterns

### command (tests 1-33) — PARTIALLY DONE
Same underlying infrastructure as sortorder. Remaining:
- [ ] `$bar`baz` tree leaf commands inherited from parent (atree.command.1-4)
- [ ] Child `$foo` blocking parent `$foo`bar` (atree.command.13-14, child masking)
- [ ] `no_command` on parent attr not masked by child `!no_command` (atree.command.19-21)
- [ ] `no_inherit` trumping no_command interaction (atree.command.27-29)
- [ ] Full "say" output verification

### sortorder & command — Test Infrastructure Notes
- Commands use `say` which outputs via `NotifyService.Notify(..., NotificationType.Say)`
- NSubstitute mocking works (proven in existing UserDefinedCommandsTests)
- Object-level `NO_COMMAND` flag vs attribute-level `no_command` flag — both checked
- Command cache (`QueryCachingBehavior`) may need invalidation after `@set obj/attr=no_command`

## Other Test Files to Port

### testsidefx.t
- Side-effect functions (set(), create(), dig(), etc.)
- Tests that functions with side effects work correctly

### testflags.t
- Object and attribute flag operations
- `@set`, `hasflag()`, `flags()`, flag permissions

### testpage.t
- `page` command (inter-player messaging)
- Message formatting, multi-target page, page-lock

## Known Implementation Notes
- `@set obj/attr=flag` supports prefix matching (e.g. "wiz" → "wizard")
- `&attr obj=val` returns empty string on both success AND permission denial (error goes via NotifyService)
- `hasattr(obj,attr)` is local-only; `hasattrp(obj,attr)` checks parents
- `no_inherit` on an attribute does NOT propagate to tree children
- Wiz flag on parent's attribute does NOT prevent child from creating local override
- Flag names are stored lowercase; comparisons use exact match for performance
- `FunctionParse` has no player context; use `CommandParse(handle, ...)` with `think [...]` for player-contextual evaluation
- Tests use unique GUID prefixes on attribute names to avoid collisions in shared session
- **NEW**: Command inheritance walks parent chain; child's local attr overrides parent's (seenNames dedup)
- **NEW**: Tree-level no_command: if `FOO` has no_command, `FOO`BAR` is also blocked from firing
