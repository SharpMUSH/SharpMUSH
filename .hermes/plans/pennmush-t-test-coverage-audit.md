# PennMUSH .t Test Coverage Audit

## testsetfuns.t — 174 tests total

### Coverage: 119 YES / 2 PARTIAL / 53 NO (68% covered)

**Covered (119 tests):**
All core setunion/setinter/setdiff/setsymdiff tests with `!` delimiter are 1:1 matched
in `ListFunctionUnitTests.cs` (lines 305-671). Space-delimiter tests without leading/trailing
whitespace edge cases are also covered.

**Missing (53 tests):**

1. **Leading/trailing whitespace handling (36 tests):** Tests like `setunion( b a,b)`,
   `setinter(b a ,b)`, `setdiff( b a , b)` that verify space trimming before set operations.
   Pattern: `.01s`, `.02s`, `.5s`, `.6s`, `.11s`-`.15s`, `.17s` across all four functions.

2. **Numeric comparison modes (9 tests):** `setunion.nums.1` through `.nums.9` test the
   4th argument sort-type parameter (i=case-insensitive, n=numeric, f=float) that changes
   how set membership is determined.

3. **Null-suffix formatting (3 tests):** `setunion.null`, `setdiff.null`, `setinter.null`
   test appending characters after the function result.

4. **ANSI-in-set operations (2 tests within nums):** `setunion.nums.8` and `.nums.9` test
   set operations on ANSI-marked strings with float comparison.

---

## testatree.t — ~130 tests total

### Coverage: ~10 YES / ~20 PARTIAL / ~100 NO (8% directly covered)

**Well-covered areas:**
- Wildcard matching (* vs ** vs backtick patterns) — `AttributeTreeWildcardTests.cs`
- Wipe removing entire subtrees — `ClearAndWipeAttributeTests.cs`
- Clear on leaves vs branches — `ClearAndWipeAttributeTests.cs`
- Basic attribute inheritance from parents — `AttributeWithInheritanceTests.cs`
- lattr() with tree patterns — `AttributeFunctionUnitTests.cs`

**Major gaps (not covered at all):**

1. **Attribute name validation (5 tests):** `atree.basic.1/2/4/5` — names ending in backtick,
   starting with backtick, double backtick. Should return "not a very good name".

2. **Branch auto-creation (3 tests):** `atree.branch.1-4` — setting `foo`bar` auto-creates
   `foo` as a branch node; `hasattr(me, foo)` returns 1 even without explicit `&foo me=`.

3. **Examine output with tree flags (8 tests):** `atree.matching.5-10, .16-17` — examine
   shows backtick flag (`) on branch attributes, ** for recursive listing.

4. **flags() showing backtick (1 test):** `atree.matching.20` — `flags(me/foo)` includes
   backtick character for branch attrs.

5. **Permission interactions on trees (20 tests):** `atree.perms.2-21` — wiz flag on
   branch prevents mortal writes to children; mortal_dark hides branches.

6. **no_inherit with trees (5 tests):** `atree.parent.22-26`, `atree.parentperms.1-5` —
   no_inherit flag blocks tree-level attribute inheritance.

7. **$command matching on trees (26 tests):** `atree.command.1-33` — user-defined commands
   stored as `$cmd` on tree attrs, no_command flag interaction, parent/grandparent lookup
   for tree commands, sort order matters for prefix matching.

8. **Sort order tests (38 tests):** `atree.sortorder.1-38` — same as command tests but
   verifies correct precedence when attrs have overlapping prefixes (abc vs abcd vs abc`xyz).

---

## Test Run Results (2026-05-04)

### ListFunctionUnitTests: 282 total, 275 pass, 7 fail
- All 40 whitespace-trimming tests PASS (SharpMUSH already trims correctly)
- All 7 failures are numeric comparison mode tests (,,i / ,,n / ,,f 4th arg)
  - SharpMUSH likely doesn't implement the sort-type parameter for set functions yet

### AttributeTreePennTests: 8 total, 3 pass, 5 fail
- PASS: Valid tree set (foo`bar), branch auto-creation (hasattr), clear branch w/ children blocked
- FAIL: Backtick name validation (3) — SharpMUSH doesn't reject bad attr names
- FAIL: Leaf clear (1) — clearing leaf attr not working as expected
- FAIL: Branch-after-leaf clear (1) — sequential clear logic issue

---

## Summary of Action Items

### High Priority (functionality gaps):
- [ ] Add tests for attribute name validation (backtick at start/end/double)
- [ ] Add tests for branch auto-creation behavior
- [ ] Add tests for $command matching on attribute trees
- [ ] Add tests for no_inherit interaction with trees

### Medium Priority (edge cases):
- [ ] Add 36 whitespace-trimming tests for set functions
- [ ] Add 9 numeric comparison mode tests for set functions
- [ ] Add tests for permission (wiz/mortal_dark) on tree branches

### Low Priority (output formatting):
- [ ] Examine output with tree flags
- [ ] flags() showing backtick on branches
- [ ] Null-suffix formatting for set functions
