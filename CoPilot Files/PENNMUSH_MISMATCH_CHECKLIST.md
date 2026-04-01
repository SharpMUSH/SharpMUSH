# PennMUSH vs SharpMUSH — Output Mismatch Checklist

**Last Updated:** 2026-03-30  
**Purpose:** Actionable checklist of known areas where SharpMUSH output diverges from PennMUSH.  
**Companion document:** `PENNMUSH_OUTPUT_COMPARISON.md` (detailed reference with exact output samples)

Each item is marked:
- `[ ]` — Not yet fixed  
- `[x]` — Fixed / verified matching  
- `[~]` — Partially fixed or deliberately different (extension)

---

## 1. Active / Confirmed Output Mismatches

These are cases where SharpMUSH is implemented but produces different output from PennMUSH.

### 1.1 Parser & Substitution

- [ ] **Q-register substitution in `NoParse`/`NoEval` mode** — `%q0`–`%qZ` registers are not
  substituted when the parser is in NoParse/NoEval mode. This breaks any softcode that stores
  `@switch` or `@dolist` bodies with `&` (RSNoParse) and then calls them — the register values
  are never expanded. Root cause: `VisitValidSubstitution` returns the raw `%qN` text when
  `ParseMode` is `NoParse` or `NoEval`.  
  **Impact:** BBS `+bbpost` `TR_POST_NOTIFY` fires `hasflag(%0,DARK)` → `#-1 CAN'T SEE THAT
  HERE` because `%0` is not substituted inside the `@switch` branch.  
  **Source:** `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs:1869-1874`

### 1.2 LOOK Command

- [ ] **Exit list uses plain comma, not Oxford comma** — PennMUSH renders the exit list as
  `East, South, and North`; SharpMUSH renders `East, South, North` (no "and" before the last
  exit).  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §3`

- [ ] **Room name shown in white ANSI** — SharpMUSH applies a white ANSI color to the room name
  header. PennMUSH shows it in plain text.  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §3`

### 1.3 WHO Command

- [ ] **Missing `Loc #` column** — PennMUSH shows the player's current room dbref; SharpMUSH does not.
- [ ] **Missing `Cmds` column** — PennMUSH shows command count since login.
- [ ] **Missing `Host` column** — PennMUSH shows hostname; SharpMUSH shows DOING text instead.
- [ ] **Footer wording** — PennMUSH: `There is one player connected.` / SharpMUSH: `1 players logged in.`  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §2`

### 1.4 SAY / POSE / Social Commands

- [ ] **`say` does not format message** — PennMUSH: speaker sees `You say, "msg"`, others see
  `Name says, "msg"`. SharpMUSH sends the raw message to all parties with no prefix.  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §5`, `SocialCommandTests.cs` (all skipped with
  "Issue with NotifyService mock")

- [ ] **`pose` does not prepend name** — PennMUSH: everyone sees `Name waves.`; SharpMUSH passes
  raw text without the name prefix.  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §6`

### 1.5 @SET Flag Output

- [ ] **Different confirmation format** — PennMUSH: `One - DARK set.` / `One - DARK reset.`.
  SharpMUSH: `Flag: DARK Set.` / `Flag: DARK Unset.` (missing object name, different casing,
  "reset" vs "Unset").  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §11`

### 1.6 Building Commands

- [ ] **`@create` message format** — PennMUSH: `Created: Object #N.` / SharpMUSH: `Created {Name} (#N).`  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §8`

- [ ] **`@dig` room number format** — PennMUSH: `TestRoom created with room number 4.` /
  SharpMUSH: `TestRoom created with room number #4.` (extra `#` sign).  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §9`

- [ ] **`@open` exit message format** — PennMUSH: `Opened exit #N` / SharpMUSH:
  `Opened exit {Name} with dbref #N.`  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §10`

### 1.7 String / Markup Functions

- [ ] **`align()` MergeToLeft (`` ` ``) and MergeToRight (`'`) column options** — These column
  merge modes are not implemented and are commented out as failing tests.  
  **Source:** `SharpMUSH.Tests/Markup/Data/Align.cs:199,212`

- [ ] **`decomposeweb()` ANSI reconstruction** — Produces incorrect output when ANSI codes
  interact with special character escaping (`\`, `%`, `;`, `[`, `]`, `{`, `}`). ANSI
  reconstruction should occur *after* text replacement, not before.  
  **Source:** `SharpMUSH.Implementation/Functions/StringFunctions.cs:1042-1044`

- [ ] **`decompose()` ANSI code `b` mis-handled** — The bold (`b`) ANSI attribute is not
  matched/reconstructed correctly.  
  **Source:** `SharpMUSH.Tests/Functions/StringFunctionUnitTests.cs:272`

### 1.8 List / Iteration Functions

- [ ] **`%iL` (last iter item) substitution** — Does not evaluate to the correct value during
  nested `iter()` calls.  
  **Source:** `SharpMUSH.Tests/Functions/ListFunctionUnitTests.cs:72`

- [ ] **`[ibreak()]` at start of iter body** — Placing `[ibreak()]` at the very start of iter
  content causes a different evaluation order vs PennMUSH.  
  **Source:** `SharpMUSH.Tests/Functions/ListFunctionUnitTests.cs:103`

- [ ] **`#@` shorthand for `inum(0)`** — Not recognized as an iter-number token; PennMUSH
  supports `#@` as a shorthand for the current iteration index.  
  **Source:** `SharpMUSH.Tests/Functions/ListFunctionUnitTests.cs:85`

### 1.9 Functions with Missing PennMUSH Names

- [ ] **`@pemit` missing `/LIST` switch** — `@PEMIT` has no `Switches = [...]` defined, so
  `@pemit/list` silently ignores the switch. PennMUSH `@pemit/list` treats the target argument
  as a space-separated list of recipients.  
  **Source:** `SharpMUSH.Implementation/Commands/GeneralCommands.cs:1183`

- [ ] **`reglmatchalli()` missing** — PennMUSH has `reglmatchalli()` which matches each word in
  a list against a regex pattern. SharpMUSH has `regmatchalli()` which is a different function
  (finds all substring matches in a single string). These are not equivalent.  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §18`

- [ ] **`ncand()` naming** — PennMUSH calls it `ncand()`; SharpMUSH calls it `cnand()`. Same
  logic, different name. Code using `ncand()` will get `#-1 FUNCTION (NCAND) NOT FOUND`.  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §18`

- [ ] **`convutcsecs()` / `convutctime()` missing** — PennMUSH has these UTC conversion
  functions; SharpMUSH does not.  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §18`

- [ ] **`xmwho()` missing** — Zone-based extended WHO function present in PennMUSH.  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §18`

- [ ] **`splice()` signature differs** — PennMUSH `splice(list1, list2, pos)` requires equal-
  length lists and interleaves them. SharpMUSH `splice()` has different argument semantics.  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §18`

- [ ] **`wordpos()` argument interpretation differs** — PennMUSH `wordpos(list, pos)` takes a
  numeric position. SharpMUSH may differ.  
  **Source:** `PENNMUSH_OUTPUT_COMPARISON.md §18`

### 1.10 Permission / Attribute System

- [ ] **`Internal` and `SAFE` attribute flags not checked in `CanSet()`** — The permission check
  does not yet enforce `INTERNAL` (system attributes can't be set by players) or `SAFE` flag
  semantics.  
  **Source:** `SharpMUSH.Library/Services/PermissionService.cs:31` (TODO comment)

---

## 2. Unimplemented Functions (Return `#-1 FUNCTION NOT FOUND`)

### 2.1 Attribute / Regex Functions

- [ ] **`foreach()`** — Iterates a string character-by-character through a user function.  
  **Source:** `SharpMUSH.Tests/Functions/MiscFunctionUnitTests.cs:24`

- [ ] **`jsonmap()`** — Maps a user function over JSON array/object keys.  
  **Source:** `SharpMUSH.Tests/Functions/MiscFunctionUnitTests.cs:42`

- [ ] **`ctu()`** — Character-to-Unicode conversion.  
  **Source:** `SharpMUSH.Tests/Functions/MiscFunctionUnitTests.cs:132`

- [ ] **`regrep()` / `regrepi()`** — Regex grep over attribute lists.  
  **Source:** `SharpMUSH.Tests/Functions/AttributeFunctionUnitTests.cs:212,222`

- [ ] **`regedit()`** — Regex-based string editing (PennMUSH equivalent of SharpMUSH's
  `regreplace()`).  
  **Source:** `SharpMUSH.Tests/Functions/AttributeFunctionUnitTests.cs:232`

### 2.2 Zone / Object Hierarchy Functions

- [ ] **Zone functions** — `zwho()`, `zmwho()`, zone-based `lattr()`/`get()` and related.
  The entire zone system is absent.  
  **Source:** `SharpMUSH.Tests/Functions/CommunicationFunctionUnitTests.cs:77`,
  `SharpMUSH.Tests/Functions/AttributeFunctionUnitTests.cs:202`

### 2.3 Parent-Mode Attribute Traversal

- [ ] **`lattr()`, `get()`, `u()` with parent-mode traversal** — Functions that walk the parent
  chain to find inherited attributes are not implemented.  
  **Source:** `SharpMUSH.Tests/Functions/AttributeFunctionUnitTests.cs:254,269,283,298,304`

---

## 3. Unimplemented Commands (Produce No Output or Exception)

### 3.1 Admin / System Commands

- [ ] `@shutdown` — Graceful server shutdown  
- [ ] `@restart` — Server restart  
- [ ] `@purge` — Remove garbage objects from DB  
- [ ] `@readcache` — Reload text file cache  
- [ ] `@flag` — Flag management (admin)  
- [ ] `@power` — Power management  
- [ ] `@hook` — Hook management  
- [ ] `@function` — Softcode function registration  
- [ ] `@command` — Command registration / modification  
- [ ] `@attribute` — Attribute definition / access control  
- [ ] `@atrlock` — Lock an attribute  
- [ ] `@atrchown` — Change attribute ownership  
- [ ] `@firstexitonly` — Exit control flag  
- [ ] `@logwipe` — Wipe log file  
- [ ] `@disable` / `@enable` — Disable or enable commands globally  
- [ ] `@hide` — Hide player from WHO  
- [ ] `@kick` — Force-idle a connection  
- [ ] `@unrecycle` — Restore a recycled object  
- [ ] `@list` — List system information  
- [ ] `@squota` — Set quota for a player  

### 3.2 Building / Object Manipulation Commands

- [ ] `@unlink` — Unlink an exit from its destination  
- [ ] `@destroy` / `@nuke` — Destroy objects  
- [ ] `@undestroy` — Cancel scheduled destruction  
- [ ] `@use` — Trigger USE lock  
- [ ] `@buy` / money system — Penny/cost system  

### 3.3 Communication / Notification Commands

- [ ] `@mail` / `@malias` — In-game mail system  
- [ ] `@message` / `@respond` — Message system  
- [ ] `@rwall` — Wizard-only broadcast  
- [ ] `@warnings` / `@wcheck` / `@suggest` — Warning system player-facing commands  
- [ ] `chat` / `@channel` / `@addcom` / `@delcom` / `@clist` / `@comlist` / `@comtitle` — Channel commands  

### 3.4 Control Flow Commands

- [ ] `@select` — Multi-option select  
- [ ] `@break` — Break out of loop/queue  
- [ ] `@assert` — Assert condition or halt  
- [ ] `@retry` — Retry current command  
- [ ] `@include` — Include another object's attribute as commands  

### 3.5 Game / Movement Commands

- [ ] `follow` / `unfollow` / `desert` / `dismiss` — Follower system  
- [ ] `goto` / `enter` — Room navigation via verb  
- [ ] `@with` — Temporary object manipulation  
- [ ] `@teach` — Trigger TEACH lock  
- [ ] `@score` / `score` — Display score/pennies  

### 3.6 Connection / Misc Commands

- [ ] `who`, `session`, `quit`, `connect` — Basic connection commands (test-level; not exercised via integration)  
- [ ] `@prompt` / `@nsprompt` — Send prompt to client  
- [ ] `@sweep` — Sweep location for listeners  
- [ ] `@edit` — Edit attribute value in-place  
- [ ] `@brief` — Toggle brief mode  
- [ ] `verb` — Trigger verb execution  
- [ ] `@http` / `@sql` / `@mapsql` / `@sockset` / `@slave` — Network/external commands (intentionally deferred)  

---

## 4. Test Infrastructure Issues (Not Output Bugs, but Block Validation)

- [ ] **`SocialCommandTests`** — All 5 tests skipped with "Issue with NotifyService mock, needs
  investigation". These cover `say`, `pose`, `whisper`, `page`, and related.
- [ ] **`CommunicationCommandTests`** — 10 tests all skipped "Not yet implemented".
- [ ] **`MailFunctionUnitTests` race condition** — 1 test skipped due to parallel test execution race.
- [ ] **`RegexFunctionUnitTests`** — 2 tests skipped requiring attribute service integration.

---

## 5. Recently Fixed (Verify Still Passing)

- [x] `&` / `@SET` notifications: now route to `executor` (not `enactor`) — matches PennMUSH
- [x] `ClearAttributeAsync` no longer double-notifies
- [x] Locate no-match: "I don't see that here." (not "I can't see that here.")
- [x] Absolute dbref (`#N`) visibility bypass — always resolvable
- [x] `CanSet` boolean logic bug — `||` vs `&&` fixed
- [x] `@trigger` argument mapping off-by-one — fixed
- [x] `&` command `RSNoParse|RSBrace` — brace literals preserved correctly
- [x] `extract()`, `words()`, `member()` whitespace-list semantics match PennMUSH
- [x] BBS `cantSeeMessages` install assertion now enforced as `IsEqualTo(0)`
- [x] `create()` / `locate()` return bare `#N` (not `#N:timestamp`)
- [x] `LocateService.Matched()` skips uncontrolled objects (preserves best match)

---

## 6. BBS Integration Test Coverage Status

The Myrddin's BBS v4.0.6 package is used as a real-world compatibility smoke test.

| BBS Command | Test Exists | Passing | Notes |
|-------------|-------------|---------|-------|
| Install script | ✅ | ✅ | `InstallMyrddinBBS` |
| `+bbread` (list groups) | ✅ | ✅ | `BBS_NewGroup_ThenBBRead` |
| `+bbnewgroup` | ✅ | ✅ | `BBS_NewGroup_ThenBBRead` |
| `+bbpost` (direct) | ✅ | ✅ | `BBS_Post_ThenBBRead` |
| `+bbread #` (list messages) | ✅ | ✅ | `BBS_BBRead_GroupScan` |
| `+bbread #/N` (read message) | ✅ | ✅ | `BBS_Post_ThenBBRead` |
| `+bblist` | ✅ | ✅ | `BBS_BBList_ShowsGroups` |
| `+bbnotify` (on/off) | ✅ | ✅ | `BBS_BBNotify_Toggle` |
| `+bbpost` (staged start) | ✅ | ✅ | `BBS_StagedPost_WriteProofToss` |
| `+bbwrite` | ✅ | ✅ | `BBS_StagedPost_WriteProofToss` |
| `+bb` (add to staged post) | ✅ | ✅ | `BBS_StagedPost_WriteProofToss` |
| `+bbproof` | ✅ | ✅ | `BBS_StagedPost_WriteProofToss` |
| `+bbtoss` | ✅ | ✅ | `BBS_StagedPost_WriteProofToss` |
| `+bbpost` (staged submit) | ✅ | ✅ | `BBS_StagedPost_WriteAndPost` |
| `+bbscan` (unread) | ✅ | ✅ | `BBS_BBScan_ShowsUnread` |
| `+bbread #/u` (unread filter) | ✅ | ✅ | `BBS_BBRead_UnreadFilter` |
| `+bbcatchup` all | ✅ | ✅ | `BBS_BBCatchup_All` |
| `+bbscan` (no unread) | ✅ | ✅ | `BBS_BBScan_NoUnread` |
| `+bbedit` | ✅ | ✅ | `BBS_BBEdit_EditsMessage` |
| `+bbsearch` | ✅ | ✅ | `BBS_BBSearch_FindsMessages` |
| `+bbtimeout` | ✅ | ✅ | `BBS_BBTimeout_SetsTimeout` |
| `+bbremove` | ✅ | ✅ | `BBS_BBRemove_RemovesMessage` |
| `+bbnewgroup` (2nd group) | ✅ | ✅ | `BBS_BBNewGroup_Second` |
| `+bbmove` | ✅ | ✅ | `BBS_BBMove_MovesMessage` |
| `+bbleave` / `+bbjoin` | ✅ | ✅ | `BBS_BBLeaveAndJoin` |
| `+bbconfig` | ✅ | ✅ | `BBS_BBConfig_ShowsAndSets` |
| `+bblock` | ✅ | ✅ | `BBS_BBLock_RestrictsGroup` |
| `+bbcleargroup` / `+bbconfirm` | ✅ | ✅ | `BBS_BBClearGroup_DeletesGroup` |

**Note:** All assertions use `Contains`/partial matching, not exact full-string matching.
The `TR_POST_NOTIFY` Q-register substitution issue (§1.1 above) is logged in the test output
but currently not asserted, so tests pass while the mismatch exists.

---

## References

- [PennMUSH source](https://github.com/pennmush/pennmush)
- [Myrddin's BBS v4.0.6](https://mushcode.com/File/Myrddins-BBS-v4-0-6)
- `CoPilot Files/PENNMUSH_OUTPUT_COMPARISON.md` — detailed output samples
- `CoPilot Files/TODO_IMPLEMENTATION_PLAN.md` — broader implementation TODO tracking
- `SharpMUSH.Tests/Integration/MyrddinBBSIntegrationTests.cs` — BBS smoke tests
