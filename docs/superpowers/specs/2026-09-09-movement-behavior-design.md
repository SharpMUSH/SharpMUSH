# Movement and Automatic Behavior — PennMUSH Parity

Status: design approved, implementation pending.
Reference: PennMUSH 1.8.8 — `src/move.c`, `src/predicat.c`, `src/lock.c`, `src/look.c`, `src/wiz.c`.

## 1. Problem

SharpMUSH's movement pipeline diverges from PennMUSH at every layer. Fifteen defects were found
by reading the two implementations side by side; the four that matter most:

- `GOTO` never calls `MoveService`. It ends at `Mediator.Send(new MoveObjectCommand(...))`, so
  walking through an exit fires no `@enter`/`@leave`/`@oenter`/`@oleave`, no `@success`/`@osuccess`
  /`@asuccess` or `@drop`/`@odrop`/`@adrop` on the exit, and produces **no automatic look**.
- `PermissionService.CanGoto` is a stub returning `true`; exit basic locks and leave locks are
  unenforced.
- No action attribute (`@aenter`, `@aleave`, `@amove`, `@atport`) ever fires from `MoveService`.
- The `ENTER` command fires its triads twice — once inside `ExecuteMoveAsync` and once inline.

The root cause is that PennMUSH's `real_did_it` primitive has no counterpart here. Its
message/omessage/action triad is hand-rolled at roughly fourteen sites, each with slightly
different executor, enactor, evaluation and audience semantics.

## 2. The `did_it` primitive

`IDidItService` in `SharpMUSH.Library/Services/`, a transcription of `real_did_it`
(`src/predicat.c:216`):

```csharp
record DidItRequest(
  AnySharpObject Player,              // enactor; %# in every evaluation
  AnySharpObject Thing,               // attribute holder; executor of what/owhat, target of awhat
  string? What = null, MString? Def = null,
  string? OWhat = null, string? ODef = null,
  string? AWhat = null,
  AnySharpContainer? Loc = null,      // defaults to Player's location
  DBRef? Env0 = null, DBRef? Env1 = null,
  IPermissionService.InteractType Interact = IPermissionService.InteractType.Hear);

ValueTask<bool> DidIt(IMUSHCodeParser parser, DidItRequest request);
```

Semantics, each one a divergence being corrected:

| Rule | PennMUSH | SharpMUSH today |
|---|---|---|
| `What` evaluated as ufun on `Thing`, executor `Thing`, enactor `Player`; result notified to `Player`; falls back to `Def` | `real_did_it` | mixed; several sites read the raw attribute without evaluating |
| `OWhat` evaluated **once**, executor `Thing`, mover's name prepended (`UFUN_NAME`), broadcast to `Loc` excluding `Player` and `Thing` | `notify_except2` | evaluated once per listener, with the *listener* as executor |
| `ODef` fallback rendered as `"<Name> <odef>"` | `safe_format` | ad hoc string concatenation |
| Whole message block skipped when `Player` is `DarkLegal` | `if (!DarkLegal(player))` | absent |
| Messages suppressed when `Loc` is not a good object; `AWhat` still runs | `GoodObject(loc)` guard placement | absent |
| `AWhat` **queued** on `Thing` with enactor `Player` | `queue_attribute_base` | run inline via `parser.With(...).CommandParse(...)` |

`FailLock(parser, player, thing, lockType, def, loc)` ports `fail_lock` (`src/lock.c:832`) with
Penn's `lock_msgs` table: `Basic`→`FAILURE`/`OFAILURE`/`AFAILURE`, `Enter`→`EFAIL`,
`Use`→`UFAIL`, `Leave`→`LFAIL`, and anything else→`<LOCK>_LOCK\`FAILURE` and siblings — which is
the rule `MoreCommands.cs:2504` already open-codes for `PAGE_LOCK\`AFAILURE`.

Supporting changes:

- `ICommunicationService.SendToRoomAsync` gains an `InteractType` parameter defaulting to `Hear`,
  so existing call sites are unchanged. Penn's movement triads use `NA_INTER_HEAR`,
  `NA_INTER_PRESENCE` and `NA_INTER_SEE` in different places; today everything is `Hear`.
- `IPermissionService.IsHearer(obj)` ports `Hearer` (`src/game.c:1564`): connected, or `Puppet`,
  or (`Audible` and has `FORWARDLIST`), or has `LISTEN`.

## 3. Movement pipeline

`MoveService` becomes a transcription of `move.c`'s three entry points, replacing the single
`ExecuteMoveAsync`:

- **`MoveIt(what, where, nomovemsgs, enactor, cause)`** — `moveit` (`move.c:66`). Computes `old`,
  `absold`, `oldSeeswhat`, `whereSeeswhat` *before* the write; performs the write; then fires, in
  Penn's exact order:
  `OXMOVE` → if `IsHearer(what)`: `LEAVE`/`OLEAVE`(default `"has left."`)/`ALEAVE` on `old` →
  `ZLEAVE`/`OZLEAVE`/`AZLEAVE` on the departed zone → `OXLEAVE` on `old` **shown in `where`**
  (only when `old` is not a room) → `OXENTER` on `where` **shown in `old`** (only when `where` is
  not a room) → `ZENTER`/`OZENTER`/`AZENTER` on the entered zone →
  `ENTER`/`OENTER`(default `"has arrived."`)/`AENTER` on `where`; if not a hearer, only `ALEAVE`,
  `AZLEAVE`, `AZENTER`, `AENTER`. Then, gated on `!nomovemsgs`, `MOVE`/`OMOVE`/`AMOVE` on the
  mover itself. Then the `OBJECT\`MOVE` event.
  The whole enter/leave block is skipped for a Dark wizard when the existing `wiz_noaenter` option
  (`CommandOptions.cs:53`, currently read by nothing) is set, and requires a good destination that
  differs from the origin.
- **`EnterRoom(...)`** — `enter_room` (`move.c:227`). Recursion guard at depth 15, `HOME`
  resolution, mobility/exit/self/containment validity checks, `MoveIt`, sticky-dropto handling,
  then **the automatic look** — unconditional, not gated on `nomovemsgs`.
- **`SafeTel(...)`** — `safe_tel` (`move.c:286`). When owners differ, sends home every carried
  object that is `STICKY` and not homed to the mover, then `EnterRoom`.

`CanMoveAsync` loses its `Controls(who, target)` requirement — Penn gates exit traversal on
`could_doit` (basic lock) and the leave lock, never on control. `PermissionService.CanGoto` gets
a real body.

Zone messages need an `AbsoluteRoom(obj)` walk, bounded by Penn's container limit; the existing
uncapped walk in `WouldCreateLoop` adopts the same bounded helper.

## 4. The automatic look

`enter_room` ends in `look_room(player, loc, LOOK_AUTO, NULL)`, synchronously. SharpMUSH's look
logic is ~380 lines inside the `Commands` partial in `SharpMUSH.Implementation`, unreachable from
`SharpMUSH.Library`.

**Decision:** extract `ILookService.LookRoom(parser, looker, loc, LookKey)` into
`SharpMUSH.Library`, with `LookKey` covering `Auto`, `Normal`, `Trans`, `CloudyTrans` and
`NoContents`. Every dependency the current `LOOK` body uses — `IAttributeService`,
`ILocateService`, `INotifyService`, `IMediator`, configuration, `HelperFunctions` — already
resolves from Library, so this is a move rather than a rewrite. `LOOK`, `EnterRoom` and
`@teleport` all call it.

`LOOK_AUTO` carries behavior currently unimplemented and gained by the extraction: `TERSE`
players skip the description and get only `@osuccess`/`@asuccess` (or `@ofailure`/`@afailure`)
rather than the `SUCCESS` triad; non-terse lookers get the full `SUCCESS` triad or `fail_lock` on
the basic lock.

`ILookService` and `IMoveService` reference each other (`LOOK` calls `RescueFromVoid`;
`EnterRoom` calls `LookRoom`). Per `engine-data-trunk.md` §5 the cycle is broken with the
`Lazy<T>` open generic already registered as `LazyService<T>` — not with a Mediator request whose
handler calls a service.

**Rejected:** queueing a `look` command via `QueueCommandListRequest`, which is what `@teleport`
does today. The look then arrives after the current queue drains rather than inline; two existing
tests already carry comments working around that interleaving
(`AttributeTreePatternVisibilityTests.cs:203`, `ChannelPermissionTests.cs:128`), and `TERSE` and
transparent-exit looks cannot be expressed. Those two workarounds are deleted by this change.

## 5. Cache and Mediator interaction

The pipeline reads contents, locations and zones repeatedly around a write, so it sits directly on
the coherence rules in `engine-data-trunk.md`. Six binding points:

1. **`OldContainer` is mandatory on every `MoveObjectCommand`.** Its `CacheTags` fall back to the
   global `CacheTags.ObjectContents` when `OldContainer` is null — wiping *every* container's
   contents list. `GOTO` omits it today, so every step through an exit performs a global contents
   wipe. `MoveIt` computes `old` before the write in any case, so it always passes it.

2. **Triads fire after the write, per Penn.** Contents reads inside them go through
   `GetContentsQuery`, whose entry is tagged `ContentsTag(#N)` and stamped by FusionCache at
   factory start (§7 of the trunk doc), so a read beginning after `CacheInvalidationBehavior` has
   run observes the post-move list. This also fixes an observable divergence: `MoveService` today
   fires the leave triad *before* the write, so softcode calling `lcon()` from `@aleave` sees the
   mover still in the room it is leaving.

3. **One evaluation per triad, not one per listener.** The current `TriggerEnterHooksAsync` and
   `TriggerLeaveHooksAsync` call `GetAttributeAsync` and `EvaluateAttributeFunctionAsync` once per
   object in the room — N permission walks, N attribute-cache reads and N parser re-entries per
   move. `DidIt` evaluates once and broadcasts the result. This is the largest cost reduction in
   the change and is also what makes `%#` correct.

4. **Snapshots versus relations.** Objects handed to `MoveIt` are snapshots (trunk §1), but
   `Location`, `Home` and `Zone` are `AsyncRelation<T>`, which re-resolves through the Mediator on
   every await and therefore follows invalidation. Post-move re-awaits yield the new location, so
   the autolook needs no re-fetch. Scalar reads off the snapshot (`Object().Name`, flags) remain
   pre-move values, which is correct for both.

5. **Queued action attributes must capture DBRefs, not snapshots.** `QueueAttributeRequest` takes a
   `Func<ValueTask<ParserState>>` evaluated when the queue drains, potentially many commands later.
   The closure builds its `ParserState` from `DBRef`s and lets resolution happen at run time; it
   must never close over an `AnySharpObject` captured before the move. `ParserState` already stores
   DBRefs, so this is a discipline rule with a test rather than a code change.

6. **`AbsoluteRoom` is computed twice per move, not per triad.** Penn computes `absold` before the
   swap and `absloc` after, once each, and reuses both for the two zone comparisons. Each hop is a
   cached, `loc:#N`-tagged `GetCertainLocationQuery`; walking per triad would multiply that.

No new request types and no new cache policy are required — `MoveObjectCommand`'s existing keys
and tags already cover every write the pipeline performs.

**Flagged, not fixed here:** `GetExitsQuery` implements neither `ICacheable` nor any tag, so the
autolook's exit listing is an uncached store read on every move. Making it cacheable needs its own
invalidation design spanning `@open`, `@dig`, `@link`, `@unlink`, `@destroy`, `@firstexit` and
exit teleport, which is a separate review.

## 6. Reentrancy and command cycles

The autolook makes movement re-enter the evaluator, and `@adescribe`/`@asuccess` let softcode move
or look again. Three cycle classes, with different guards.

### 6.1 Synchronous function recursion — guarded, must not be broken

`@describe` of `u(%#/describe)` looked at by its own owner, or two players whose `@describe` each
evaluate the other's, recurse inside a single evaluation. `AttributeService` already guards this:
`ParserState.FunctionRecursionDepths` is keyed by **attribute name** and checked against
`Limit.FunctionRecursionLimit` (default 100), alongside the shared `CallDepth` counter and
`LimitExceeded` flag. Because the key is the attribute name rather than the object, the two-player
mutual case is caught by the same counter as the self-referential one.

The guard only holds while those three collections propagate. Therefore:

- **`ILookService.LookRoom` takes the caller's `IMUSHCodeParser` and threads its state.** It must
  not build a fresh `ParserState` the way `EventService.cs:139` and `ConnectionAnnounceService.cs:354`
  conditionally do — that resets `FunctionRecursionDepths` on every hop, turning a bounded
  recursion into stack exhaustion.
- **`IDidItService.DidIt` likewise threads the caller's parser** for `What` and `OWhat`. PennMUSH's
  `real_did_it` allocates a fresh `pe_info` here, which is safe there only because its action
  attribute is queued rather than called; SharpMUSH keeps the state so a `did_it` inside an
  evaluation cannot reset the depth counters.

A test covers each: self-referential `@describe`, and mutual `@describe` between two objects, both
terminating with the recursion error rather than a stack overflow.

### 6.2 Synchronous movement recursion — guard needed, but not Penn's

`enter_room` recurses through `moveit`'s `HOME` case into `safe_tel`, and through
`send_contents`/`maybe_dropto` once per content. PennMUSH bounds it with `static int deep` capped
at 15 (`move.c:232`).

**A literal port of that is wrong here.** Penn's counter is process-global and correct only because
its queue is single-threaded and non-reentrant; SharpMUSH runs moves concurrently, so a static
counter would let one player's deep move abort an unrelated player's shallow one, and would leak
across requests. The depth lives in `ParserState` beside `CallDepth`, or is threaded as an explicit
parameter through `EnterRoom`/`SafeTel`/`SendContents`. Either is acceptable; a static or
`AsyncLocal` counter is not.

### 6.3 Queued command cycles — currently unbounded, and the reason to fix the queue

`@adescribe` of `look %#` on two objects that look at each other, or two rooms whose `@asuccess`
teleports the looker to the other, produce an unbounded chain of queue entries. Each entry is a
fresh parser state, so §6.1's counters reset every hop by design — the queue quota is the only
backstop, exactly as in PennMUSH, where `queue_limit` trips `"Runaway object: %s(%s). Commands
halted."` and halts the object (`cque.c:303`).

**SharpMUSH has no such backstop.** `LimitOptions.PlayerQueueLimit` exists, defaults to 100, is
documented in `sharptop.md:1313` — and is read by nothing but configuration plumbing and tests.
`TaskScheduler` instead holds one process-wide bounded channel of 10,000 entries written with
`TryWrite`, so a runaway fills the channel and every later enqueue — including ordinary player
commands from unrelated players — is silently discarded behind a `LogWarning` that misattributes
the cause to the queue being completed.

Moving action attributes onto the queue (§2) makes this reachable from more places than before, so
it is fixed as part of this work:

- enforce `PlayerQueueLimit` per owner in `TaskScheduler`, counting that owner's pending entries;
- on breach, notify the owner with Penn's runaway message, log it, and set `HALT` on the object;
- give wizards and objects holding the `Queue` power the `player_queue_limit + database size`
  allowance that `sharptop.md:1313` already documents;
- correct the overflow log message, which currently states a cause that is not the one that fired.

Choosing to queue action attributes rather than run them inline is also what keeps this class of
cycle recoverable at all: an inline `@adescribe` of `look %#` would recurse on the .NET stack and
take the process down, where a queued one is bounded by the quota above.

Tests: mutual `@adescribe`-look between two objects halts the offender and leaves an unrelated
player's queued command unaffected; a wizard receives the larger allowance; the queue survives the
runaway rather than dropping subsequent work.

## 7. Call-site rewiring

- **`GOTO`** gains the `do_move` path it lacks: leave lock → `FailLock(Leave)`; `could_doit` →
  `FailLock(Basic)`; `SUCCESS`/`OSUCCESS`/`ASUCCESS` on the exit; `DROP`/`ODROP`/`ADROP` on the
  exit with `Loc` set to the destination; `EnterRoom` for a room destination or `SafeTel` for a
  player/thing destination; and, when the mover's location actually changed, `follower_command`
  (`move.c:1300`) re-issuing `GOTO` through the same exit for each object following the mover,
  which is what drives the existing but currently unreachable `FOLLOW`/`UNFOLLOW` commands.
- **`@teleport`** — switch `QUIET` becomes `SILENT` (Penn's name); attributes become
  `TPORT`/`OTPORT`/`ATPORT`/`OXTPORT`; the hand-queued `look` is deleted in favour of
  `EnterRoom`'s autolook. `silent` suppresses `MOVE`/`OMOVE`/`AMOVE` and the `TPORT` triad, and
  nothing else — not `ENTER`/`LEAVE`, not the autolook.
- **`ENTER` / `LEAVE`** lose their duplicated inline triads (the double-fire) and delegate to
  `EnterRoom`.
- **`HOME`** gains Penn's three `"There's no place like home..."` lines and the
  `"<Name> goes home."` broadcast, gated on the mover and location both being non-Dark.
- **Full sweep:** `GET`, `DROP`, `GIVE`, `USE`, `BUY`, `PAGE`, `LOOK`, `@name` and the
  connect/disconnect announcements adopt `DidIt`/`FailLock` in place of their hand-rolled triads.

## 8. Removed inventions

Not present in PennMUSH and deleted rather than kept behind a flag; this is pre-release software:

- `"You sense that you have moved from <X> to <Y>."` (`MoveService.NotifyContentsOfMoveAsync`)
- `"You enter <name>."` (`MoreCommands` `ENTER`)
- The `OTELEPORT` / `OXTELEPORT` attribute names, which have no PennMUSH counterpart.
- The `@teleport/QUIET` switch name.

## 9. Testing

The seven `[Skip]`-ed stubs in `MoveServiceTests.cs` get real bodies against the DB-backed
harness. Each divergence in §1–§7 gets a test written before its fix:

- exit traversal produces departure and arrival messages and an automatic look;
- `@enter` fires `@oenter` exactly once;
- `OXENTER` set on a container is shown in the origin room, `OXLEAVE` in the destination;
- neither fires when the container is a room;
- a `DarkLegal` mover produces no o-messages but still fires action attributes;
- a non-hearing object fires `@aenter` and sends no messages;
- `@teleport/silent` suppresses `@amove` but not `@aenter`, and still autolooks;
- an exit whose basic lock fails runs `@failure`/`@ofailure`/`@afailure`, evaluated, not raw;
- a failed leave lock runs `@lfail`/`@olfail`/`@alfail`;
- `@oleave` softcode calling `lcon()` on the origin does not see the mover;
- an action attribute queued by a move resolves its executor at drain time, not at capture time;
- a move through an exit does not invalidate unrelated containers' contents caches.

## 10. Out of scope

- Making `GetExitsQuery` cacheable (§5).
- `@dolist`-style follower edge cases beyond `follower_command` on a successful move.
- Multi-node cache coherence, which the trunk doc already defers to a FusionCache backplane.
- Replacing `TaskScheduler`'s single process-wide channel with per-owner queues; §6.3 adds the
  quota and the runaway halt to the existing structure without restructuring it.
