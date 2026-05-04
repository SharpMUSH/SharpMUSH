# PennMUSH Compatibility — Status & Next Steps

## Branch: `pennmush-compatibility`
## Last Updated: 2026-05-04
## Tests: 3430 passing, 0 failing

---

## Completed

### Phase 1A: .t file audit & test porting
- **testsetfuns.t**: 282 tests ported (whitespace trimming, numeric comparison modes, all set ops)
- **testatree.t**: 8 tests ported (name validation, branch auto-creation, clear semantics)
- **testmath.t**: 106 tests ported. Fixes:
  - CallState(double) always formats with G{FloatPrecision} (consistent 15 sig figs)
  - tan(90,d) singularity detection (|result| > 1e15 threshold)
  - log() supports base `e`, negative/zero range checks, `-inf` formatting
  - ln() negative/zero range checks
  - baseconv() handles negative numbers for bases ≤ 36
  - baseconv() normalizes +/ → -_ for base 64 input (PennMUSH accepts both)
  - fraction() uses continued fraction approximation algorithm
- **testfirstof.t**: 25 tests ported (firstof, strfirstof, allof, strallof)
- **testreswitch.t**: 12 tests ported (reswitch/reswitchall/reswitchi/reswitchalli with regex)
- **testdistxd.t**: 2 new dist2d/dist3d tests added (non-trivial distances)
- **testjust.t/testlnum.t**: ljust/rjust truncation (4th arg) and lnum float support fixed
  - ljust/rjust: pass TruncationType.Truncate when 4th arg is truthy
  - lnum: rewritten to support float start/end/step via double.TryParse
- All 12 initial failures FIXED:
  - Set function `IEqualityComparer<string>` system for sort-type-aware membership
  - Attribute name backtick validation (leading/trailing/consecutive rejected)
  - `&attr obj=` respects `empty_attrs` config option
  - Sort type `a`/`i` mapping corrected
  - SortService: cached comparers, Ordinal comparison, InvariantCulture parsing

### Known Deliberate Incompatibilities (do NOT "fix" these)
1. Mid-string function recognition (SharpMUSH extension)
2. PE_COMPRESS_SPACES applies to literal text nodes only, not function return values
3. (Others documented in earlier sessions — check session_search if needed)

---

## Next Steps (in priority order)

### 1. Continue Phase 1A: Audit more .t files
Each PennMUSH `.t` file in `pennmush/test/` is a goldmine of behavioral specs.
Run them against PennMUSH oracle: `cd pennmush/test && perl runtest.pl <file>.t`

**Unaudited .t files (high value):**
- `testfuns.t` — general function tests (likely hundreds of tests)
- `teststring.t` — string functions (mid, left, right, edit, etc.)
- ~~`testmath.t` — math functions~~ ✅ DONE (106 tests ported)
- `testbool.t` — boolean/logic functions
- `testcmds.t` — command parsing
- `testlocks.t` — lock evaluation
- `testpcre.t` — regex functions

**Process for each .t file:**
1. Parse the .t file format (think/command → expected output)
2. Identify which tests already pass in SharpMUSH (run against existing test suite)
3. Write new TUnit tests for uncovered behaviors
4. Fix failures holistically (not if-else patches)

### 2. Phase 1A gaps in already-audited files

**testatree.t remaining (from audit):**
- [ ] $command matching on attribute trees (26 tests) — HIGH
- [ ] Sort order for tree command matching (38 tests) — HIGH
- [ ] Permission interactions (wiz/mortal_dark) on trees (20 tests) — MEDIUM
- [ ] no_inherit with trees (5 tests) — MEDIUM
- [ ] Examine output with tree flags (8 tests) — LOW
- [ ] flags() showing backtick on branches (1 test) — LOW

**testsetfuns.t remaining:**
- [ ] ANSI-in-set operations (2 tests) — MEDIUM
- [ ] Null-suffix formatting (3 tests) — LOW

**testsort.t remaining:**
- [ ] Float sort treats non-numeric items as 0 (`sort(list,f)`) — MEDIUM
- [ ] ANSI-aware sort (sort.3, sort.4) — MEDIUM

### 3. Phase 1B: Function parity (systematic)
- Enumerate ALL functions in PennMUSH's `fun_tab[]` vs SharpMUSH's `FunctionLibrary`
- Identify missing functions
- Verify argument counts, flags (NoParse, Literal, etc.)

### 4. Phase 2: Parser parity
- Escape sequences, nested evaluation, %substitutions
- Edge cases: empty args, trailing commas, unbalanced parens

### 5. Phase 3: Command parity
- @commands, single-token commands (&, @, etc.)
- Switch/case handling, command queue semantics

### 6. Phase 4: Edge cases & error messages
- All `#-1` errors in ALL CAPS, no trailing periods (ErrorMessages.Returns)
- Notify messages in sentence case (ErrorMessages.Notifications)

---

## Key Architecture Notes for Future Sessions

- **Test runner**: `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ClassName/*"`
- **PennMUSH oracle**: `cd pennmush/test && perl runtest.pl testfile.t`
- **Grammar MUST NOT change** — structure lives in ANTLR4, semantics in the visitor
- **TUnit source generator** has file-size limits — very large test files may not discover tests added at the end (split into multiple classes if needed)
- **Config source of truth**: `SharpMUSH.Tests/Configuration/Testfile/mushcnf.dst`
- **Error centralization**: `SharpMUSH.Library/Definitions/ErrorMessages.cs`
- **SortService**: comparers are cached singletons, use Ordinal comparison, InvariantCulture for numbers
