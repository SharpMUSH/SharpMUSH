# PennMUSH Compatibility — Remaining Work

## Status: In Progress

## Command Tree Inheritance (testatree.t) — DONE

### Tests Implemented
- [x] `lattrp` returns full parent attribute tree (6 oracle cases)
- [x] Parent inherited command fires on child (atree.command.9)
- [x] `no_command` on attribute blocks tree descendants (atree.command.16-17, sortorder.17-18)
- [x] Child command masks parent command (atree.command.13-14, sortorder.14-15)
- [x] `no_inherit` on parent tree root causes fallthrough to grandparent (atree.command.27-29, sortorder.28-30)
- [x] `no_command` on parent tree root blocks leaf inheritance (atree.command.19-21, sortorder.20-22)

### Handler Changes (GetCommandAttributesQueryHandler)
- [x] Two-pass scan: pre-compute no_inherit and no_command prefixes, then process (order-independent)
- [x] Tree-level no_inherit: parent's flagged attr + all `` ` ``descendants skipped, falls to grandparent
- [x] Tree-level no_command: flagged attr + all `` ` ``descendants blocked from inheritance
- [x] `seenNames` dedup ensures child overrides parent (exact name only, not tree descendants)

### Remaining (low priority, covered by existing logic)
- [ ] `no_command` directly on leaf attr (sortorder.24-26) — logic works, could add explicit test
- [ ] `@wipe` + cache invalidation (sortorder.36-38) — cache infra handles this, could add test
- [ ] Child clears attr + parent has no_command + no_inherit → falls to grandparent (sortorder.32-34)

## Other Test Files to Port

### testsidefx.t
- Side-effect functions (clone(), set(), create(), dig(), etc.)
- `@clone` and `clone()` with /preserve flag
- Verify dangerous flags not copied without /preserve

### testflags.t
- Object and attribute flag operations
- `@set`, `hasflag()`, `flags()`, flag permissions
- Already has test coverage in FlagAndPowerCommandTests.cs and FlagFunctionUnitTests.cs

### testpage.t
- `page` command — messaging between players
- Currently skipped in test suite

## Infrastructure Notes
- UserDefinedCommandsTests marked `[NotInParallel]` due to shared NSubstitute mock
- Tests use unique tokens in command output for isolation
- FusionCache invalidation via `ICacheInvalidating` on all attr/flag mutation commands
- Test config: `CacheInvalidationOptions.InvalidateAfterHandler = true` in test env
