# PENNMUSH COMPATIBILITY
SharpMUSH targets PennMUSH 1.8.8 softcode compatibility. Where it differs, the difference is one of
three things, and this profile keeps them apart:

  **A choice.** Deliberate, and not going to change. Listed with what PennMUSH does, what SharpMUSH
  does instead, why, and how to write code that works on both.<br>
  **Unresolved.** The difference is known and the decision has not been made. Do not write code that
  depends on either behaviour.<br>
  **A defect.** Tracked by an issue. It will change.

Every entry here can be checked from inside the game; the examples are lines you can type. Where an
example needs a second player, God or a server setting, the entry names the case in the parity harness
(`tools/parity/scenarios/`) that runs it on PennMUSH and SharpMUSH side by side.

  [COMPATIBILITY CONFIG]    settings that change how your code evaluates<br>
  [COMPATIBILITY PARSER]    evaluation, dispatch and error handling<br>
  [COMPATIBILITY COMMANDS]  command tables, configuration and handlers<br>
  [COMPATIBILITY ARGUMENTS] functions that take a different number of arguments<br>
  [COMPATIBILITY IDENTITY]  dbrefs, objids, time precision and number precision<br>
  [COMPATIBILITY OUTPUT]    rendering a string for something outside the game<br>
  [COMPATIBILITY ECONOMY]   money, pennies and costs<br>
  [COMPATIBILITY NAMES]     functions and commands that exist here and not there<br>
  [COMPATIBILITY MAIL]      @mail forwarding, filters and folders<br>
  [COMPATIBILITY UNRESOLVED] known differences with no decision yet<br>
  [COMPATIBILITY DEFECTS]   known differences that are bugs, with their issue<br>
  [COMPATIBILITY MATCHED]   differences that used to exist and no longer do

# COMPATIBILITY CONFIG
These configuration options change how existing code evaluates. All of them are read at evaluation
time, so changing one takes effect on the next call rather than at the next restart. Set them with
`@config/set` or on the web portal's configuration page.

## Boolean compatibility — `tiny_booleans`

Boolean conditions use the same rules in functions, command guards, list filters, and indirect calls.
With `tiny_booleans` off, numeric zero (including `-0`, `0.0` and hexadecimal zero), an empty string,
space-only text, and values beginning `#-` are false. Other text is true, including the word `false`.
A preserved tab is text, and a trailing space makes an otherwise numeric value text.

With `tiny_booleans` on, a leading signed integer determines truth. Thus `text` and `0.1` are false,
while `1.2text` is true. The compatibility conversion uses a signed 64-bit prefix, saturates
overflow, then takes its low 32 bits. For example, `4294967296` is false.

```sharp
> think t(-0.1)
1
> think t(-0)
0
```

`neq()` is the numeric inverse of `eq()`: it is true when not all arguments are equal. `condall()`
returns every result with a true condition; `ncond()` and `ncondall()` select false conditions. A
condition is a single value, not a list of separate booleans. A selected empty result does not cause
the default to run.

## Numeric compatibility — `tiny_math`, `null_eq_zero`

Arithmetic and numeric comparisons read `tiny_math` and `null_eq_zero` at call time, including
indirect `#apply` calls. With `tiny_math` off, the complete argument must be a number; an empty
argument is zero only when `null_eq_zero` is on. Scientific notation is accepted. Real-valued
arguments also accept hexadecimal notation, such as `0x10` and `-0x1.8p2` (16 and -6).

With `tiny_math` on, the leading numeric prefix is used, and text without one becomes zero:
`add(12foo,2)` returns 14 and `add(foo,2)` returns 2. Integer arithmetic takes an integer prefix, so
`div(1.5,1)` returns 1. Each function retains its existing signed, unsigned, decimal or
floating-point range; this setting does not remove range checks or give decimal arithmetic support
for infinity. Numeric input and output use a decimal point regardless of the server's language
settings.

`inc()` and `dec()` adjust a signed integer suffix. Without a suffix, they append 1 or -1 only when
`null_eq_zero` is on; otherwise they report an integer suffix error. Overflow reports
`#-1 OUT OF RANGE`. `tiny_math` does not change these string-counter rules.

## Trim argument order — `tiny_trim_fun`

With `tiny_trim_fun` off, `trim(text,characters,side)` uses Penn argument order. With it on,
`trim(text,side,characters)` uses Tiny argument order. The default characters are spaces and the
default side is both; `l` and `r` select the left or right side.

**Workaround.** Use `trimpenn(text,characters,side)` or `trimtiny(text,side,characters)` when a
package needs a fixed argument order regardless of game configuration.

```sharp
> think trimpenn(xxhixx,x,l)
hixx
```

## Parenthesis groups — `paren_groups`

**A choice.** Off by default; importing a PennMUSH database turns it on.

**PennMUSH** treats a `(` that starts no function call as opening a literal group: its commas are
text, and its `)` does not close the call around it, so `cat(x,(a,b)c)` has two arguments.<br>
**SharpMUSH**, with `paren_groups` off, treats that `(` as plain text. The first unescaped `)` closes
the call and the commas inside separate arguments, so the same call has three.<br>
**Why.** Whether a parenthesis groups then depends only on whether it follows a function name, and a
literal parenthesis is always written the same way. Imported worlds keep Penn's behaviour so migrated
softcode runs unchanged.<br>
**Workaround.** Escape literal parentheses inside function arguments with `\(` `\)` or `%(` `%)`.

```sharp
> think cat(x,(a,b)c)
x (a bc)
> think cat(x,%(a%,b%)c)
x (a,b)c
> think cat(x,\(a\,b\)c)
x (a,b)c
```

With the option on, the unescaped form groups as PennMUSH's does:

```sharp paren_groups
> think cat(x,(a,b)c)
x (a,b)c
```

# COMPATIBILITY PARSER
Deliberate differences in how a line is parsed, dispatched and evaluated. SharpMUSH does not intend
to change any of these to match PennMUSH.

## The command word is not evaluated for built-in dispatch

**A choice.**

**PennMUSH** evaluates the first word of a command line before matching it, so
`[strcat(th,ink)] hello` runs `think`.<br>
**SharpMUSH** matches the built-in command word literally.<br>
**Why.** A command name that only exists after evaluation cannot be checked against a lock, a hook or
a restriction before it runs.<br>
**Workaround.** Build command names as literal text, or dispatch through `@switch`.

```sharp
> think [strcat(th,ink)] hello
think hello
```

This is specifically about matching a built-in command name. It does not contradict the `$`-command
rule in [COMPATIBILITY MATCHED]: once built-in dispatch has not matched, `$`-commands *are* matched
against the fully evaluated line — a separate, later stage.

## Malformed expressions are an error, not silent text

**A choice.**

**PennMUSH** evaluates almost anything — an unclosed `[`, `(`, or `{`, or a trailing `\` still
produces output.<br>
**SharpMUSH** answers `#-1 PARSER FAILURE` for an unbalanced expression.<br>
**Why.** Silent recovery turns a typo into output that looks deliberate.<br>
**Workaround.** Balance your brackets. Orphaned closers with no opener, such as a stray `]`, are
still literal text, as in PennMUSH.

```sharp
> think [add(1,2)
#-1 PARSER FAILURE: ...
> think add(1,2)]
3]
```

## Function arguments are not evaluated before an argument-count error

**A choice.**

**PennMUSH** runs the arguments for their side effects first: `not(setq(0,x),1)` sets `%q0` and then
reports the arity error.<br>
**SharpMUSH** validates the argument count first, so those side effects do not happen.<br>
**Why.** A miscalled function should not change the world.<br>
**Workaround.** Do not rely on side effects in the arguments of a miscalled function.

```sharp
> think [not(setq(0,x),1)]%q0
#-1 FUNCTION (NOT) EXPECTS AT MOST 1 ARGUMENTS BUT GOT 2
```

## Evaluation stops at the first limit

**A choice.**

**PennMUSH** continues past a function-invocation, recursion, call-depth or output limit.<br>
**SharpMUSH** stops evaluating the rest of the expression.<br>
**Why.** The limits exist to bound the work a single line can cause; continuing past one does not.<br>
**Workaround.** Expect no output after the point where a limit is reached.

An attribute that calls itself reaches the recursion limit. PennMUSH goes on to append an `x` for
every call that had already started and counts the result, answering 86 here; SharpMUSH stops:

```sharp
> &RECURSE me=[u(me/RECURSE)]x
> think strlen(u(me/RECURSE))
#-1 FUNCTION RECURSION LIMIT EXCEEDED
```

## `##` in `iter()` is data, not code

**A choice.**

**PennMUSH** splices each list element into the pattern as text and then evaluates it, so element
text containing softcode executes.<br>
**SharpMUSH** binds `##` to the iteration register (equivalent to `%iL`) and does not re-evaluate
element text.<br>
**Why.** Splice-then-evaluate is a code-injection path: any list whose contents a player can
influence becomes executable.<br>
**Workaround.** Use `u()` or an explicit evaluation where you meant to run the element.

```sharp
> think iter(a b,##)
a b
> &CODE me=[add(1,2)]
> think iter(CODE,u(##))
3
```

## Function names cannot be produced by evaluation

**A choice.**

**PennMUSH** builds the called function's name from evaluated output, so `[setq(0,add)]%q0(1,2)`
calls `add`.<br>
**SharpMUSH** recognises function names lexically; a name that only appears after substitution is
ordinary text.<br>
**Why.** The same reason as the command word: a call that does not exist until evaluation cannot be
restricted.<br>
**Workaround.** `switch()` on the name, or store the call in an attribute and `u()` it.

PennMUSH answers `3` to the first line:

```sharp
> think [setq(0,add)]%q0(1,2)
add(1,2)
> think switch(add,add,add(1,2),sub,sub(1,2))
3
```

## A literal tab after `(` or `,` is layout

**A choice.**

**PennMUSH** keeps a tab character typed straight after a function's `(` or an argument's `,`, and
trims only spaces there, so `strlen(<tab>a)` is 2.<br>
**SharpMUSH** treats a tab in that position like a newline, as layout that separates tokens, and
drops it: `strlen(<tab>a)` is 1, and `strlen(<tab>)` is 0 where PennMUSH answers 1. A tab anywhere
else in an argument is kept, so `strlen(a<tab>b)` is 3 on both.<br>
**Why.** It lets softcode be indented over several lines, with tabs, without changing what it
evaluates to.<br>
**Workaround.** Write the tab as `%t`, or put anything evaluated, even an empty `[space(0)]`, in front
of it. `%t` is kept in every position on both servers:

```sharp
> think strlen(%ta)
2
> think strlen(a%tb)
3
```

## Unescaped commas are never absorbed by a final argument

**A choice.**

**PennMUSH** lets the last argument of functions such as `pemit()`, `emit()` and `capstr()` swallow
extra unescaped commas — `capstr(a,b,c)` capitalises the string `a,b,c` — though it now warns that
this is deprecated.<br>
**SharpMUSH** treats every comma as an argument separator, so `capstr(a,b,c)` is a
too-many-arguments error.<br>
**Why.** It avoids silently changing what counts as an argument.<br>
**Workaround.** Escape the commas (`\,`) or brace the text.

```sharp
> think capstr({a,b,c})
A,b,c
```

Where SharpMUSH gives a function an optional argument PennMUSH does not have (see
[COMPATIBILITY ARGUMENTS]), a PennMUSH-era call with an unescaped comma does not fail: it passes that
argument instead. `strlen()` is one.

## A command that crashes says so

**A choice.**

**PennMUSH** has no equivalent.<br>
**SharpMUSH** answers `#-1 EXCEPTION: <json>` when an internal error escapes a command, and notifies
the player with the same text.<br>
**Why.** Producing no output at all is indistinguishable from a command that did nothing on
purpose.<br>
**Workaround.** See `help exception` for the payload and what a mortal versus a wizard is shown.<br>
**No example.** A command that crashes is a defect, and none is kept around to show this with. Once
one is found it is fixed, so there is no command whose output could stand as the example.

## A failing function says why

**A choice.**

**PennMUSH** answers many failures with a bare `#-1` (or `#-2`, `#-3`), sometimes notifying the
reason separately and often not giving one at all.<br>
**SharpMUSH** puts the reason after the number: `#-1 PERMISSION DENIED`, `#-1 NO MATCH`,
`#-1 NO ZONE SET`, `#-1 NO DROP-TO`, `#-2 I DON'T KNOW WHICH ONE YOU MEAN`, `#-2 VARIABLE
DESTINATION`, `#-3 HOME`, and so on. Where PennMUSH notified a reason and returned a bare `#-1`, as
`hidden()` did, the reason is now the return value and nothing is notified. A function that looks an
object up still says "I can't see that here." when the match fails, as PennMUSH's does.<br>
**Why.** A bare `#-1` reads the same whether the object was missing, refused, or simply had nothing
to report, and a notification sent from inside `iter()` arrives once per element.<br>
**Workaround.** Test the prefix, as PennMUSH's own test suite does: `strmatch(%0,#-*)`, or `t()`,
which is false for anything starting `#-`. Code that compares with `=` or `eq()` against exactly
`#-1` needs the prefix test instead. `namelist()` keeps one-word `#-1` and `#-2` entries, because each
entry is a position in a list.

```sharp
> think zone(me)
#-1 NO ZONE SET
> think [strmatch(zone(me),#-*)] [t(zone(me))]
1 0
```

## A set function's delimiter can be longer than one character

**A choice.**

**Affects** `setunion() setdiff() setinter() setsymdiff()`.<br>
**PennMUSH** answers `#-1 SEPARATOR MUST BE ONE CHARACTER` to a `<delimiter>` longer than one
character, once either list is non-empty.<br>
**SharpMUSH** splits both lists on the whole `<delimiter>` and joins the answer with it, unless an
`<osep>` is given.<br>
**Why.** Data held in softcode lists often already contains every single character.<br>
**Workaround.** Use a one-character `<delimiter>`; it means the same on both servers.

```sharp
> think setunion(a::b,b::c,::)
a::b::c
> think setinter(a::b,b::c,::)
b
```

## A Wizard's `mailstats()` is their own

**A choice.**

**Affects** `mailstats() maildstats() mailfstats()` with no `<player>`.<br>
**PennMUSH** turns a Wizard's empty `<player>` into "all mail" (`extmail.c:2238-2242`), then
rejects that as no player before its all-mail branch is reached. The Wizard is told ": No such
player." and gets nothing back.<br>
**SharpMUSH** gives a Wizard their own statistics, as it gives everyone else.<br>
**Why.** `help mailstats()` documents `mailstats([<player>])` as your own statistics, and PennMUSH's
answer is an error nobody could have relied on.<br>
**Workaround.** None needed: `mailstats(me)` means the same on both servers.

```sharp
> think words(mailstats())
2
```

# COMPATIBILITY COMMANDS
Deliberate differences in the command table, the configuration and the objects the game starts with.

## Added commands do not run `UNIMPLEMENTED_COMMAND`

**A choice.**

**PennMUSH** runs a command made with `@command/add` and never `@hook`ed through the
`UNIMPLEMENTED_COMMAND` entry (`command.c:1900-1903`), so a hook on `UNIMPLEMENTED_COMMAND` changes
what every such command does.<br>
**SharpMUSH** has the added command print "This command has not been implemented." itself. A hook on
`UNIMPLEMENTED_COMMAND` changes only what typing `UNIMPLEMENTED_COMMAND` does, and that can be typed
directly.<br>
**Why.** Both print the same text, so only a hook on `UNIMPLEMENTED_COMMAND` can tell them apart.
Re-entering hook dispatch by command name from inside a command body needs a mechanism only
`HUH_COMMAND` has. (#1257)<br>
**Workaround.** `@hook` the added command itself rather than `UNIMPLEMENTED_COMMAND`.

```sharp unchecked
> @command/add foo
> foo
This command has not been implemented.
```

## `@config/set` is stored

**A choice.**

**PennMUSH** changes the running value; the change is lost at restart unless it is also written to
`mush.cnf`.<br>
**SharpMUSH** keeps one stored configuration, which the portal also edits, and every `/set` is written
to it and lasts across restarts. `/save` does the same and says so.<br>
**Why.** There is no `mush.cnf` for a running game to fall back to.<br>
**Workaround.** Set the value back when a change was meant to be temporary.<br>
**No example.** The difference shows only after a restart, and the parity harness starts each server
once per run.

## `command_restrictions` reapplies without a restart

**A choice.**

**PennMUSH** applies the `restrict_command` lines of `mush.cnf` at startup and restart (`game.c:757`,
`bsd.c:1284`). `@config/set restrict_command=<command> <restriction>` applies one more while the game
runs (`cmds.c:334-335`, `conf.c:866-896`). Each one replaces the command's restriction
(`command.c:1752-1760`). There is no list to take a line back out of, so nothing returns a command to the
restriction it was made with.<br>
**SharpMUSH** keeps the lines as one setting, `command_restrictions`. `@config/set restrict_command`
adds a line to it, or replaces the line for that command, and applies it at once, as PennMUSH does. The
setting is applied at startup, before `@STARTUP` runs, as PennMUSH does, and again whenever it is changed
from the portal or `@config/set`, in both directions: a command the change no
longer restricts goes back to the restriction it was made with, and is enabled again if the
configuration had disabled it with `nobody`. Every command the old or the new setting names starts
again from the restriction it was made with, so a live `@command/restrict` on one of those commands
is lost. Commands the setting does not name, and changes to other options, leave live restrictions
alone. (#1250) `@config/set restrict_command` with words that are neither restriction words nor a
valid lock says `Couldn't set that option.`; PennMUSH says `Option set.` and leaves the command
unlocked.<br>
**Why.** The configuration can be changed while the game is running; a restriction that waits for a
restart looks as if it had been ignored. A mistyped restriction is refused rather than leaving the command
open to everyone.<br>
**Workaround.** After changing `command_restrictions`, repeat any `@command/restrict` that should
still apply to a command it names, or put the restriction in `command_restrictions` itself.<br>
**Example.** The parity case `choice.restrictions-reapply` in `tools/parity/scenarios/40-compat-choices.scn` runs it on both servers.

## A new game restricts no commands

**A choice.**

**PennMUSH** ships `restrict.cnf` and includes it from `mush.cnf`, so a stock game already applies 21
`restrict_command` lines at startup (`game/restrictcnf.dst`): a guest cannot `@create`, `@dig`,
`@open`, `@set`, `@link` or `@lock`, a FIXED player's `home` is refused with `You can't do that IC!`,
and `@destroy` is `noplayer` with its own message, so a player who types `@destroy <thing>` is
answered `Use @recycle instead` and the thing is not destroyed (`game/restrictcnf.dst:86`, sent by
`command.c:2336-2339`).<br>
**SharpMUSH** ships `command_restrictions` empty. Every command in a new game is open to whatever its
own permission check allows: a player's `@destroy <thing>` destroys the thing, and a guest can
`@create`. An imported game keeps its own: reading a PennMUSH `mush.cnf` follows its `include` lines,
as `config_file_startup` does, and carries each `restrict_command` line into `command_restrictions`
and each `restrict_function` line into `function_restrictions`, and both are applied at startup, so
an imported `restrict_function lstats noguest` answers a guest's `lstats()` with
`#-1 PERMISSION DENIED` as PennMUSH does. A `!`-prefixed `restrict_function` word, which in PennMUSH
clears one of a built-in's own restrictions, is logged and ignored. A line that cannot be carried (an include that cannot be read, a restriction with no value,
a `restrict_attribute`) is logged by name, and so is every `include` in a `mush.cnf` uploaded through
the portal, which is not followed because it would name a file on the server.<br>
**Why.** Those lines are one site's policy, not the engine's behaviour — `restrict.cnf` is a file the
administrator edits, and two PennMUSH games rarely ship the same one. Taking `@destroy` away from
ordinary players on a fresh install is the game's decision to make, and SharpMUSH does not make it for
a world nobody has configured yet.<br>
**Workaround.** Put the lines back, in the words PennMUSH writes them:
`@config/set restrict_command=@destroy noplayer " Use @recycle instead`, one per command, or set
`command_restrictions` from the portal's configuration page. `noguest`, `nofixed`, `noplayer`,
`nobody`, a flag or power name, a lock and the `" <message>` suffix all mean there what they mean in
`restrict.cnf`, and the restriction applies at once and lasts across restarts. The same goes for
`function_restrictions` from the portal, with `noguest`, `nogagged`, `nofixed`, `admin`, `wizard`,
`god` and `nobody`.<br>
**Example.** The parity case `obj.create` in `tools/parity/scenarios/10-player-commands.scn` runs it on both servers.

## PennMUSH's file and allocator housekeeping answers `NOT SUPPORTED`

**A choice.**

**PennMUSH** trims its own log files with `@logwipe` and reports its attribute-chunk allocator with
`@stats/chunks`, `/regions`, `/paging` and `/freespace`.<br>
**SharpMUSH** writes its logs to the sinks named in its configuration and stores attributes in its
database, so it has neither. Those commands return `#-1 NOT SUPPORTED`, and `@logwipe` also records
the attempt in the server log.<br>
**Why.** Nothing in the game owns the resource those commands manage.<br>
**Workaround.** Rotate logs where they are configured.<br>
**Example.** The parity case `choice.housekeeping` in `tools/parity/scenarios/40-compat-choices.scn` runs it on both servers.

## The HTTP and event handlers exist from the start

**A choice.**

**PennMUSH** has no HTTP or event handler until a wizard creates an object and points
`http_handler` or `event_handler` at it.<br>
**SharpMUSH** creates the HTTP Handler (#8) and the Event Handler (#9) in a new database, with the
options already pointing at them and the default HTTP verb attributes installed.<br>
**Why.** The web portal is built on handler routes and needs them present.<br>
**Workaround.** Extend the shipped handlers rather than creating new ones. See `help http` and
`help event`.

A new PennMUSH database has no `#8` or `#9`, and answers `#-1 NO SUCH OBJECT VISIBLE` twice:

```sharp
> think [name(#8)]|[name(#9)]
HTTP Handler|Event Handler
```

## A drop-to move's event names who caused it

**A choice.**

**PennMUSH** reports the move of an object sent through a drop-to as caused by `SYSEVENT`
(`move.c:187`).<br>
**SharpMUSH** has no `SYSEVENT` object and passes on the enactor whose action caused the move. This
shows only in the enactor of the `OBJECT`MOVE` event. (#1006 item 9)<br>
**Why.** There is no `SYSEVENT` dbref to name.<br>
**Workaround.** Do not rely on an `OBJECT`MOVE` handler's enactor to tell a drop-to move from any
other.<br>
**Example.** The parity case `choice.dropto` in `tools/parity/scenarios/40-compat-choices.scn` runs it on both servers.

## Typed input is bounded by a pending count, not a replenishing rate

**A choice.**

**PennMUSH** keeps a typed line out of the queue entirely — `run_user_input` hands its
`QUEUE_SOCKET` entry straight to `do_entry` (`cque.c:1076-1088`), so it never touches the tally
`queue_limit` reads — and bounds it on the descriptor instead: a burst of `COMMAND_BURST_SIZE`
commands that replenishes at `COMMANDS_PER_SECOND` (`conf.h:99-100`, `bsd.c:197,1000-1004`).<br>
**SharpMUSH** also keeps typed input out of `player_queue_limit`, so a full owner queue never stops
its owner typing and a typed line is never why another of that owner's commands is refused. It
bounds it by socket with `command_burst_size`, which defaults to the same 100 but caps the number
of typed lines *outstanding* rather than the rate at which they start. A connection over that cap
is told `Queue admission rejected: ConnectionLimit.` (#1320)<br>
**Why.** SharpMUSH queues typed input rather than running it inside the network loop, so a pending
count is the quantity it has; a replenishing rate would delay input the queue is already ready to
run.<br>
**Workaround.** None needed for softcode. Raise `command_burst_size` if a client that sends large
scripted bursts is refused.

PennMUSH answers `#-1 NO SUCH CONFIG OPTION`:

```sharp
> think config(command_burst_size)
100
```

# COMPATIBILITY ARGUMENTS
Functions that accept a different number of arguments here. Every one of these is **additive**: the
call you would write for PennMUSH keeps its PennMUSH meaning, and the extra argument is optional.

`SharpMUSH.Tests/PennMUSH/function-arity.tsv` holds PennMUSH's own declared arity for every
function, and a test compares it against the registered arity on every run, so this list cannot
silently grow. See `docs/guides/registry-parity-and-coverage.md`.

## A trailing `<precision>` on the time functions

**A choice.**

**Affects** `convtime() convutctime() csecs() etime() etimefmt() idle() msecs() secs() stringsecs()
timestring() uptime()`.<br>
**PennMUSH** answers whole seconds.<br>
**SharpMUSH** answers whole seconds too, unless you ask for another unit in one more trailing
argument: `s` (the default), `ms`, `f` for fractional.<br>
**Why.** SharpMUSH stores creation and modification times to the millisecond, so `csecs()` truncating
to seconds would make `[num(%0)]:[csecs(%0)]` fail to reconstruct an objid. Rather than change what a
PennMUSH call returns, the finer value is a separate request.<br>
**Workaround.** Omit the argument for PennMUSH behaviour.

```sharp unchecked
> think secs()
1789000000
> think secs(ms)
1789000000123
```
(The numbers are whatever the clock says; what matters is that the second is the first with three
more digits.)

## A trailing `<osep>` on the vector functions

**A choice.**

**Affects** `vadd() vsub() vmul() vdot() vmax() vmin() vcross()`.<br>
**PennMUSH** joins the answer with the same `<delimiter>` it split the inputs on.<br>
**SharpMUSH** takes a fourth argument that overrides it, defaulting to `<delimiter>`.<br>
**Why.** Every other list function in the game spells the same idea that way.<br>
**Workaround.** Omit it.

```sharp
> think vadd(1|2|3,4|5|6,|)
5|7|9
> think vadd(1|2|3,4|5|6,|,%b)
5 7 9
```

## Prepared-statement parameters on `sql()` and `mapsql()`

**A choice.**

**PennMUSH** takes at most four arguments and rejects a fifth.<br>
**SharpMUSH** binds every argument past the fourth as a prepared-statement parameter, substituted for
a `?` in the query.<br>
**Why.** The alternative is softcode concatenating values into query text.<br>
**Workaround.** None needed; a four-argument call behaves as PennMUSH's does.

```sharp unchecked
> think sql(lit(SELECT name FROM people WHERE id = ?),%b,%b,%b,7)
```

## A trailing `<count controls>` on `strlen()`

**A choice.**

**PennMUSH** takes one argument. A second one is joined to the first, commas and all, with a
warning that this is deprecated: `strlen(a%tb,0)` warns, then answers 5.<br>
**SharpMUSH** takes an optional second argument. A false value counts control characters such as
`%t` and `%r` as zero, as `displaywidth()` does, so `strlen(a%tb,0)` is 2. Omitted or true, each
control character counts as one, as in PennMUSH. (#1256)<br>
**Why.** A tab or a newline takes up no columns, and code that pads a table needs that width.<br>
**Workaround.** Omit the argument for PennMUSH behaviour. A PennMUSH-era call that relied on an
unescaped comma being kept needs it escaped: `strlen(a\,0)`.

```sharp
> think strlen(a%tb)
3
> think strlen(a%tb,0)
2
> think strlen(a\,0)
3
```

For Latin-1 text the two servers count alike. Beyond it, PennMUSH counts the bytes of the UTF-8
encoding and SharpMUSH counts display columns: a wide CJK character is two and a combining mark is
none. PennMUSH answers `6` here:

```sharp
> think strlen(漢字)
4
```

## Argument-less forms PennMUSH does not offer

**A choice.**

**PennMUSH** answers `stext()` with `#-1 ARGUMENT MUST BE INTEGER`, and declares `lwhoid()` with at
most one argument, so `lwhoid(me,online)` is an argument-count error.<br>
**SharpMUSH** treats `stext()` as `stext(0)`, the innermost switch, and lets `lwhoid()` take the
`<status>` argument `lwho()` takes.<br>
**Why.** An omitted level means the innermost one everywhere else. PennMUSH serves `lwho()` and
`lwhoid()` with one C function and only declares them differently.<br>
**Workaround.** Write `stext(0)`, and give `lwhoid()` at most one argument, for code that has to run
on both.

```sharp
> think switch(a,a,stext())
a
> think switch(a,a,stext(0))
a
```

## `ansi()` and `lit()`

PennMUSH spells "the last argument swallows the rest, commas and all" as a negative maximum in its
function table. SharpMUSH reaches the same result with a literal flag on the function instead, so the
declared maximum is not a comparable number. Behaviour matches; only the declaration differs.

# COMPATIBILITY IDENTITY
## Time precision

SharpMUSH stores object creation and modification times to the millisecond. PennMUSH stores whole
seconds. Every function that reports one of those times answers whole seconds by default — see
[COMPATIBILITY ARGUMENTS] — so a PennMUSH call gets a PennMUSH answer.

The consequence worth knowing: an objid carries the millisecond value, so
`[num(<obj>)]:[csecs(<obj>)]` does **not** reconstruct one here, where in PennMUSH it does. Ask for
the field the objid actually holds:

```sharp unchecked
> think [num(me)]:[csecs(me,ms)]
#1:1789000000123
```
That is the same two fields as `objid(me)`; whole seconds are not:

```sharp
> think strmatch(objid(me),[num(me)]:[csecs(me,ms)])
1
> think strmatch(objid(me),[num(me)]:[csecs(me)])
0
```

## Objids and stamped dbrefs

SharpMUSH's `objid` is `<dbref>:<creation time in milliseconds>`. Where PennMUSH's `unparse_dbref`
produces a bare dbref in a message, SharpMUSH may produce the stamped form; the two are not
interchangeable as text. Parse an objid with `num()` when you want the dbref alone. The
representation choice is deliberate and tracked separately in #1006 item 13.

## A destroyed object's dbref is not reused

**A choice.**

**PennMUSH** turns a destroyed object into garbage when it is purged and puts its dbref on a free
list; `new_object()` takes the next object from that list (`free_get()`, `src/db.c`,
`src/destroy.c`), so the most recently purged dbref is the next one created. `isdbref()` answers 1
for a garbage dbref, because `parse_objid()` accepts any dbref inside the database (`fun_isdbref`,
`src/fundb.c`).<br>
**SharpMUSH** keeps no garbage objects: a purged object's row is removed. A new object takes the next
dbref after the highest one ever allocated, so a purged object's dbref is not handed out again unless
a wizard or a holder of the `Pick_DBRefs` power names it (`@create <name>=<cost>,<dbref>`), and
`isdbref()` answers 0 for it.<br>
**Why.** A dbref stored in an attribute, a lock or a list keeps pointing at nothing once its object
is gone, instead of silently pointing at whatever was created next.<br>
**Workaround.** Do not rely on a new object taking a destroyed object's dbref, and test whether a
dbref still names an object with `isdbref()`, not by comparing against old dbrefs.

Until the purge, the object is only set `GOING`, and both servers answer `isdbref()` with 1. Here the
second `@nuke` of a `GOING` object purges it at once:

```sharp unchecked
> @create Scratch
Created Scratch (#50:1789000000123).
> @nuke #50
> @nuke #50
> think isdbref(#50)
0
> @create Scratch
Created Scratch (#51:1789000000456).
```

In PennMUSH the same lines answer `1` and create `#50` again.

## Number precision

`float_precision` is the number of decimal places every floating-point result is written with,
trailing zeros dropped, as in PennMUSH. SharpMUSH defaults to 6, the same as PennMUSH, and caps the
setting at 15. `round()` cannot ask for more places than the setting allows.

```sharp
> think pi()
3.141593
> think round(pi(),6)
3.141593
```

## A result that rounds to zero from below is `0`

**A choice.**

**PennMUSH** writes it `-0`, the sign C's `printf` leaves on it.<br>
**SharpMUSH** writes `0`.<br>
**Why.** `-0` and `0` are the same number, but compared as text, by `switch()`, `strmatch()` or a
`$`-command pattern, they are different answers.<br>
**Workaround.** Compare numbers with `eq()`, which is true for `-0` and `0` on both servers.

PennMUSH answers `-0` to the first line:

```sharp
> think fdiv(-1,10000000000000000)
0
> think eq(-0,0)
1
```

## Fraction representation

**A choice.**

**PennMUSH** answers the *simplest* fraction within one part in 10^10: `frac()` walks the
convergents of a continued fraction and stops at the first one inside that tolerance, which its own
help states — "dividing the numerator by the denominator of the results will not always return the
original `<number>`, but something close to it".<br>
**SharpMUSH** answers the **exact** rational of its argument, reduced. Where no simpler fraction is
within PennMUSH's tolerance the two agree, which covers every number of six decimal places or fewer;
where one is, SharpMUSH stays exact and PennMUSH does not.<br>
**Why.** Every number softcode can hand it is a decimal, so an exact rational always exists, and
dividing the result out gives back exactly the number that went in.<br>
**Workaround.** `round()` the number first if you want a simpler fraction than the one it names.

```sharp
> think fraction(pi())
3141593/1000000
> think fraction(0.3333334)
1666667/5000000
> think fraction(-2.75)
-11/4
```

PennMUSH answers `348987/111086` for the first of those, off by 1.8e-11.

# COMPATIBILITY OUTPUT
`render(<string>, <formats>)` converts a string's markup for something outside the game — a bot, a
web page, an SQL column. The formats are `ansi`, `html`, `noaccents` and `markup`, as in PennMUSH,
and `ansi` requires the `Can_Spoof` power.

**One difference.** PennMUSH's `markup` flag asks for whatever the other flags did not handle to
survive as internal markup tags. SharpMUSH holds markup as layers over the text rather than as inline
tags, and renders a whole string to one format, so there is nothing for the flag to leave behind: it
is accepted and changes nothing.

```sharp
> think render(ansi(r,a<b>c),html)
<span style="color: #aa0000">a&lt;b&gt;c</span>
> think render(ansi(r,red),markup)
red
```

Rendering to a format the game does not otherwise use is how you get a portable string: `html` for a
web page, `ansi` for a terminal capture, neither for plain text.

# COMPATIBILITY ECONOMY
## `money()` is not supported

**A choice.**

**PennMUSH** keeps a penny balance on every player: `money()` reports it, and building, `give`,
`@pay` and `buy` spend it.<br>
**SharpMUSH** keeps no balance. `money()` returns `#-1 NOT SUPPORTED` and tells you why.<br>
**Why.** SharpMUSH does not track pennies, so there is no balance to report.<br>
**Workaround.** Keep balances in attributes. Code that only displays `money()` should test it for
`#-1` first.

```sharp
> think money(me)
#-1 NOT SUPPORTED
```

# COMPATIBILITY NAMES
Functions and commands that exist in SharpMUSH and not in PennMUSH, or that take their arguments in a
different order.

The additions below cost imported code nothing — a name PennMUSH never had cannot be called by
PennMUSH softcode. **The argument-order difference is not in that class.** `ctitle()` and `cstatus()`
take their two arguments the other way round here, so every existing call to either has to be
swapped by hand; nothing detects it for you, and a call left alone will resolve the channel name as
an object and answer an error or the wrong object's status. It is the one entry in this profile that
requires migrating code you already have.

## Argument order: `ctitle()` and `cstatus()`

**A choice.**

**PennMUSH** is `ctitle(<channel>, <object>)` and `cstatus(<channel>, <object>)`.<br>
**SharpMUSH** is `ctitle(<object>, <channel>)` and `cstatus(<object>, <channel>)` — object first,
consistently across the pair.<br>
**Why.** No reason was recorded when the order was chosen. It stays because reversing it now would
break code written for SharpMUSH.<br>
**Workaround.** Swap the arguments when importing channel softcode.

On a game with a `Public` channel you have joined:

```sharp unchecked
> think cstatus(me,Public)
ON
```

## `wshtml()` accepts but does not use `<default string>`; there is no `wsjson()`

**A choice.**

**PennMUSH**'s `wshtml(<html>, <default>)` and `wsjson(<json>, <default>)` write the payload into the
line as an out-of-band region and the `<default>` as the reading for a client without WebSockets,
because its output buffer holds raw bytes and cannot degrade a tag on its own. `think` shows only the
`<default>`.<br>
**SharpMUSH**'s output is markup, which can. `wshtml(<html>)` parses the fragment into tags over text,
the same value `tagwrap()` builds one tag at a time: a WebSocket, Pueblo or MXP client gets the tags,
an ANSI client gets the styling it can show for `<b>`, `<i>`, `<u>` and `<s>` and the words for
everything else, and everything else gets the words. The `<default>` argument is accepted, so
softcode written for PennMUSH runs unchanged, but it is not used: a client without HTML sees the
fragment's own text rather than `<default>` (`wshtml(<b>x</b>,y)` reads `x`, where PennMUSH shows
`y`). `wsjson()` does not exist.<br>
**Why.** The words of the fragment are already its plain reading, so there is no second string to
supply. JSON is data for a program, not text with a plain reading.<br>
**Workaround.** If the plain reading matters, make the fragment's own text say it. Send JSON with
`oob()`, which delivers it where a connection can receive it, over GMCP or the WebSocket.

PennMUSH shows nothing for the first line:

```sharp
> think wshtml(<b>hi</b>)
hi
```

## SharpMUSH-only functions

  `motd() wizmotd() downmotd() fullmotd()` — the Messages of the Day `@motd` set. Only `motd()` is
  readable by a mortal.<br>
  `cinfo(<channel>[, <field>])` — one field of a channel: `name`, `owner`, `members` or `buffer`.<br>
  `cnand()` — the cancelling form of `nand()`, for code written against servers that spell it so.<br>
  `zfind(<zone>[, <osep>])` — the objects `@chzone`'d to a zone that you may examine.<br>
  `decomposeweb()` — `decompose()` for a web client: angle brackets encoded, colour rebuilt as an
  `ansi()` call.<br>
  `regreplace()` — regex replace with `$1`-style backreferences and an `i` flag, where PennMUSH has
  `regedit()` with alternating pairs and `%1`-style references.<br>
  `regmatchalli()` — a second name for `reglmatchalli()`. Despite the name it searches a list and
  returns positions; it is not a case-insensitive `regmatch()`.<br>
  `rendermarkdown()` — CommonMark to markup.<br>
  `formq()`, the wiki functions and the scene functions — SharpMUSH subsystems with no PennMUSH
  counterpart.

Two are registered and do nothing yet: `websocket_html()` and `websocket_json()` validate their
arguments and return an error. Use `oob()` for GMCP. `objmem()` always answers 0.

## SharpMUSH-only commands

  `@account` — administers web-portal accounts.<br>
  `@locale` — the language the server addresses you in.<br>
  `@map` — `@dolist` passing the element as `%0` rather than substituting it.<br>
  `register`, `login`, `make`, `play` — the account layer at the login screen.<br>
  `version` — `@version` before you have connected. PennMUSH has no bare `version`; it is accepted
  here because crawlers and players from MUX-family servers type it, and it publishes nothing `INFO`
  does not.<br>
  `~<command>` — run one command under strict parsing.

## SharpMUSH-only flags and powers

**A choice.**

**PennMUSH** has no `APPROVED`, `SCENE_ROOM` or `TRUECOLOR` flag and no `See_OOB` or `Unkillable`
power.<br>
**SharpMUSH** defines all five, so `@list flags`, `@list powers`, `list(flags)` and `list(powers)` name
them among PennMUSH's own, in the same sorted, comma-separated line.<br>
**Why.** `APPROVED` is what `isapproved()` reads, `SCENE_ROOM` belongs to the scene plugin and
`TRUECOLOR` marks a client that takes 24-bit colour; `See_OOB` and `Unkillable` are reserved for
SharpMUSH subsystems. An imported PennMUSH database has none of them set.<br>
**Workaround.** None needed for imported code: nothing PennMUSH wrote can set or test a name it never
had. Code that walks `list(flags)` should expect names it does not know, as it already must for a
game's own `@flag/add` flags.

**Example.** The parity case `admin.lists` in `tools/parity/scenarios/20-admin-commands.scn` lists
both, and the harness accepts exactly these five names on SharpMUSH's side.

# COMPATIBILITY MAIL
`@mail` matches PennMUSH's commands and switches. Three differences are deliberate, and all three are
about what happens to a message between the sender's `@mail` and the recipient's folder.

## A `MAILFILTER` that mails its owner does not recurse

**A choice.**

**PennMUSH** runs `filter_mail` (`extmail.c:3289`) on every delivery with no reentrancy guard, so a
filter that sends mail to its own owner filters that message too, and so on.<br>
**SharpMUSH** delivers mail sent from inside a filter unfiltered: the message lands in the inbox and
no filter runs for it.<br>
**Why.** The captured PennMUSH run crashed the server. Nothing useful depends on the recursion.<br>
**Workaround.** None needed. A filter that files its own notifications must do so by sending to the
folder it wants rather than by expecting its own filter to run again.<br>
**No example.** PennMUSH's side crashed the server when it was captured, and a crash would end the
parity run.

## Folders are named freely and created on delivery

**A choice.**

**PennMUSH** routes a filter's answer through `do_mail_file` (`extmail.c:616-630`), whose
`parse_folder` (`extmail.c:2839-2855`) accepts a digit `0`–`MAX_FOLDERS` or the name of a folder the
player has already used, and answers `MAIL: Invalid folder specification` otherwise.<br>
**SharpMUSH** accepts any alphanumeric name (`extmail.c:333`'s own rule for one) and makes it one of
the player's folders as the message is filed, exactly as `@mail/file` does.<br>
**Why.** A filter is written before the folder it files into exists; requiring the player to create it
first means the first message that matches is the one that goes astray.<br>
**Workaround.** Filters written for PennMUSH keep working. A filter that returns a name PennMUSH would
have rejected files here instead of erroring, so check the spelling — a typo makes a folder.<br>
**Example.** The parity case `choice.mail-folder` in `tools/parity/scenarios/40-compat-choices.scn` runs it on both servers.

## An empty `MAILFORWARDLIST` is no list

**A choice.**

**PennMUSH** reads `&MAILFORWARDLIST me=` as a forward list naming nobody, and `empty_attrs`
(`extmail.c:1479`, `:1483`) means every message to that player is then dropped.<br>
**SharpMUSH** treats a whitespace-only value as no list at all, and mail is delivered normally. The
list is read without parents either way, so a parent's list never forwards a child's mail.<br>
**Why.** One `&MAILFORWARDLIST me=` should not silently stop a player's mail.<br>
**Workaround.** Nothing to change in code that never wrote an empty list. Do not reach for an empty
`MAILFORWARDLIST` as a way to stop receiving mail: it stops nothing here.<br>
**Example.** The parity case `choice.mail-forwardlist` in `tools/parity/scenarios/40-compat-choices.scn` runs it on both servers.

# COMPATIBILITY UNRESOLVED
Known differences with **no decision recorded**. Do not write code that depends on either behaviour;
either may change.

## `$`-command scheduling: queued or inline (#1132)

**PennMUSH** queues a matched `$`-command's action and runs it from the queue.<br>
**SharpMUSH** executes the body inline, at the point of the match.<br>
**What follows from it.** Command ordering relative to other queued work, register isolation,
recursion depth and the effect of `@halt` and `@wait` can all differ.<br>
**Status.** #1132 records the source-path difference and asks for either the queued behaviour or an
explicit decision that inline dispatch is intended. Neither has happened. The paired transcripts that
would show the effects have not been captured.

Until it is settled, do not rely on a `$`-command body running before or after anything else queued
by the same line.

## Connection hooks run inline (#1006 item 14)

`ConnectionAnnounceService` evaluates connection hooks inline rather than admitting them to the
scheduler. Same shape of question as #1132, same lack of a decision.

## Argument-count error wording

PennMUSH writes four different messages depending on the function's declared range — `EXPECTS <n>`,
`EXPECTS <min> OR <max>`, `EXPECTS AT LEAST <min>`, `EXPECTS BETWEEN <min> AND <max>` (parse.c:2981).
SharpMUSH writes `EXPECTS AT LEAST <min>` or `EXPECTS AT MOST <max>`. Softcode that matches on the
text of an arity error will not port. No decision has been recorded on whether to adopt PennMUSH's
four forms.

## HALT and players (#1006 items 1 and 2)

**PennMUSH** queues an object's `@a`-action even when it is `HALT`ed, exempts players from the halted
check (`cque.c:530`), and sets `HALT` on any runaway, a player included (`cque.c:303-313`).<br>
**SharpMUSH** queues no action for a `HALT`ed object of any type, so that `@halt` stops an object
completely. As a consequence it never sets `HALT` on a runaway player, which would silence them.<br>
**Status.** The code records both as deliberate. #1006 asks whether to adopt PennMUSH's player
exemption, which would let a runaway player be halted too. The two change together.

## `EMPTY` on a `STICKY` item (#1006 item 4)

**PennMUSH**'s `do_empty` sends the *container* home in that case (`move.c:845-847`).<br>
**SharpMUSH** sends the item home.<br>
**Status.** #1006 asks for an explicit decision rather than copying PennMUSH's surprising behaviour.

## Queue fairness between owners (#1006 item 15)

Admission is bounded by global and per-owner limits, and a refused entry is reported with its reason.
Whether one owner's backlog should be able to delay another's, as it can in PennMUSH, is an open
design question.

## `regrabi()`

PennMUSH declares `REGRAB` and `REGRABALL` as taking up to four arguments and `REGRABI` as taking
three. SharpMUSH matches that exactly, including the inconsistency. Whether to give `regrabi()` the
fourth argument is undecided.

# COMPATIBILITY DEFECTS
Differences that are **bugs**, tracked and expected to change. Listed so they are not mistaken for
choices.

  **`pcreate()` takes no third argument.** PennMUSH accepts an optional dbref to reuse. (#974)<br>
  **`attrib_set#()` cannot be called.** The parser's function-name token does not admit `#`, so the
  text is returned unchanged. Use `attrib_set()`. (#974)<br>
  **`objmem()` always answers 0.** (#974)<br>
  **`buy` has no economy.** It is a stub. (#1006 item 10)<br>
  **`TERSE` is an alias of `CLOUDY`.** PennMUSH has them as two flags sharing the letter `x`, so
  `@list flags` lists `TERSE (x)` there and not here. (#1404)<br>
  **The vacation flag is named `ON_VACATION`.** PennMUSH names it `ON-VACATION`; both spellings are
  accepted here, but lists show the other one. (#1405)

The other movement and queue gaps left by the movement work are enumerated in #1006 rather than
repeated here; its items 1, 2, 4 and 15 are in [COMPATIBILITY UNRESOLVED].

# COMPATIBILITY MATCHED
These once differed and now match PennMUSH; noted here only because earlier SharpMUSH releases
behaved differently.

- Imported locks keep PennMUSH's creator and its protected, inheritable, visual and no_clone
  flags (#1108).
- Lock operator precedence: `&` binds tighter than `|`, so `a & b | c` is `(a & b) | c`.
- `textentries()` is `textentries(<type>, <pattern>[, <osep>])`: the pattern is required and
  filters the topic names. `textentries()` and `textfile()` also refuse an unknown `<type>` and
  gate the administrator-only `ahelp` corpus on wizard or royalty, as PennMUSH's `admin` help
  files are.
- `hasattr()`, `hasattrp()`, `hasattrval()` and `hasattrpval()` take the whole
  `<object>/<attribute>` spec in one argument as well as the two-argument form; one argument
  carrying no `/` is `#-1 BAD ARGUMENT FORMAT TO <function>`.
- `letq()` requires an odd number of arguments and `setr()` an even number; `case()`/`caseall()` have
  no parity requirement.
- An unknown function name outside `[...]` is left as literal text (`think foo(bar)` prints
  `foo(bar)`); inside `[...]` it is `#-1 FUNCTION (FOO) NOT FOUND`.
- A HALTED object runs none of its softcode — `u()`/`ufun()` return the stored attribute text
  *unevaluated* (PennMUSH's `PE_NOTHING`: `u()` of a halted object's `[add(1,2)]` yields the literal
  `[add(1,2)]`), while its `$`-commands do not fire. `@halt <object>` and `@chown` (which halts to
  break ownership loops) rely on this.
- An uppercase substitution selector capitalizes the first output character: `%Q0`, `%N`, `%I0`, `%S`
  capitalize; `%q0`, `%n`, `%i0`, `%s` do not.
- `% ` (a percent followed by a space) is emitted literally as `% `.
- `$`-commands are matched against the command line *after* it is evaluated, so a command whose name
  or arguments only appear once substitutions and functions run still matches and captures its
  `%0..` from the evaluated text.
- An unknown function inside `[...]` whose name is close to a real one is reported as
  `#-1 FUNCTION (NAME) NOT FOUND DID YOU MEAN 'CLOSEST'`.
- `%c` is the running command as written, and `%u` is the last command after its arguments were
  evaluated, rebuilt as `NAME/switches arguments` (`@PEMIT/silent me=2`;
  `ATTRIB_SET/<attribute>`, `SAY`, `GOTO <exit>` for the token and exit forms; the typed word and
  evaluated rest for a command nothing matched). A command's own arguments therefore see the
  *previous* command's `%u`; its hooks and anything it runs see its own. Each queued entry and each
  `$`-command body starts with both empty, while `@include` and `@ifelse` share them with the list
  that ran them.
- Argument minimums. The boolean and bitwise families (`and() or() xor() cand() cor() nand() ncand()
  ncor() nor() band() bor() bxor() bnot()`), `cat()`, `strcat()`, `lit()`, `t()`, `loc()`, `locks()`
  and `attrib_set()` once accepted a call with no arguments at all; they now enforce PennMUSH's
  minimum. `max()` and `min()` once demanded two arguments; they now fold over one, as PennMUSH does.
- The vector family (`vadd() vsub() vmul() vdot() vmax() vmin()`) once answered the empty string for
  every call.
- `isword()` once answered 1 only for a single letter.
- `checkpass()` once required a dbref and refused a player name.
- `avg() cname() element() exp() hostname() replace() reverse() speakpenn()` each had a help topic
  calling them another name for an existing function, and each failed when called: the alias table
  had been copied into three places and none of the three carried them.
- `render()` once evaluated its second argument as the object named by its first, which is what
  `objeval()` does, rather than rendering a string.

## Known limitations

Not yet at parity; may change in a future release.

- **`%c` and `%u` inside `@trigger` and locks.** `@trigger` runs its list in place, so that list
  starts from the triggering command's `%c`/`%u` instead of empty ones. A lock evaluated for a
  `$`-command sees neither. `%u` also shows the value of `&attr obj=value` and `@attr obj=value` as
  written, where PennMUSH shows it evaluated in a queued list, and a computed `&` attribute name as
  written (`ATTRIB_SET/[CAT(F,OO)]`), where PennMUSH shows the name it evaluated to
  (`ATTRIB_SET/FOO`).
- **Characters above U+FFFF** (emoji and other supplementary-plane characters) are stored as UTF-16
  surrogate pairs. This is internally consistent, but a substitution or slice that lands between the
  two halves of a pair could split it. Rare in practice.

## See also
- `help @halt`
- `help iter()`
- `help substitutions`
- `help exception`
