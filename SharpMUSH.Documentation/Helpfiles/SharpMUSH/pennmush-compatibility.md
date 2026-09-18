# PENNMUSH COMPATIBILITY
SharpMUSH targets PennMUSH 1.8.8 softcode compatibility. Where it differs, the difference is one of
three things, and this profile keeps them apart:

  **A choice.** Deliberate, and not going to change. Listed with what PennMUSH does, what SharpMUSH
  does instead, why, and how to write code that works on both.<br>
  **Unresolved.** The difference is known and the decision has not been made. Do not write code that
  depends on either behaviour.<br>
  **A defect.** Tracked by an issue. It will change.

Every entry here can be checked from inside the game; the examples are lines you can type.

  [COMPATIBILITY CONFIG]    settings that change how your code evaluates<br>
  [COMPATIBILITY PARSER]    evaluation, dispatch and error handling<br>
  [COMPATIBILITY ARGUMENTS] functions that take a different number of arguments<br>
  [COMPATIBILITY IDENTITY]  dbrefs, objids, time precision and number precision<br>
  [COMPATIBILITY OUTPUT]    rendering a string for something outside the game<br>
  [COMPATIBILITY NAMES]     functions and commands that exist here and not there<br>
  [COMPATIBILITY UNRESOLVED] known differences with no decision yet<br>
  [COMPATIBILITY DEFECTS]   known differences that are bugs, with their issue<br>
  [COMPATIBILITY MATCHED]   differences that used to exist and no longer do

# COMPATIBILITY CONFIG
Three configuration options change how existing code evaluates. All three are read at evaluation
time, so changing one takes effect on the next call rather than at the next restart.

## Boolean compatibility — `tiny_booleans`

Boolean conditions use the same rules in functions, command guards, list filters, and indirect calls.
With `tiny_booleans` off, numeric zero (including `-0`, `0.0` and hexadecimal zero), an empty string,
space-only text, and values beginning `#-` are false. Other text is true, including the word `false`.
A preserved tab is text, and a trailing space makes an otherwise numeric value text.

With `tiny_booleans` on, a leading signed integer determines truth. Thus `text` and `0.1` are false,
while `1.2text` is true. The compatibility conversion uses a signed 64-bit prefix, saturates
overflow, then takes its low 32 bits. For example, `4294967296` is false.

```sharp
> think t(0.1)
0
> @config/set tiny_booleans=1
> think t(1.2text)
1
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

# COMPATIBILITY PARSER
Deliberate differences in how a line is parsed, dispatched and evaluated. SharpMUSH does not intend
to change any of these to match PennMUSH.

## The command word is not evaluated for built-in dispatch

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

**PennMUSH** continues past a function-invocation, recursion, call-depth or output limit.<br>
**SharpMUSH** stops evaluating the rest of the expression.<br>
**Why.** The limits exist to bound the work a single line can cause; continuing past one does not.<br>
**Workaround.** Expect no output after the point where a limit is reached.

## `##` in `iter()` is data, not code

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

**PennMUSH** builds the called function's name from evaluated output, so `[setq(0,add)]%q0(1,2)`
calls `add`.<br>
**SharpMUSH** recognises function names lexically; a name that only appears after substitution is
ordinary text.<br>
**Why.** The same reason as the command word: a call that does not exist until evaluation cannot be
restricted.<br>
**Workaround.** `switch()` on the name, or store the call in an attribute and `u()` it.

## Unescaped commas are never absorbed by a final argument

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

## A command that crashes says so

**PennMUSH** has no equivalent.<br>
**SharpMUSH** answers `#-1 EXCEPTION: <json>` when an internal error escapes a command, and notifies
the player with the same text.<br>
**Why.** Producing no output at all is indistinguishable from a command that did nothing on
purpose.<br>
**Workaround.** See `help exception` for the payload and what a mortal versus a wizard is shown.

## A failing function says why

**PennMUSH** answers many failures with a bare `#-1` (or `#-2`, `#-3`), sometimes notifying the
reason separately and often not giving one at all.<br>
**SharpMUSH** puts the reason after the number: `#-1 PERMISSION DENIED`, `#-1 NO MATCH`,
`#-1 NO ZONE SET`, `#-1 NO DROP-TO`, `#-2 I DON'T KNOW WHICH ONE YOU MEAN`, `#-2 VARIABLE
DESTINATION`, `#-3 HOME`, and so on. A function does not also notify you of a reason its return value
already carries.<br>
**Why.** A bare `#-1` reads the same whether the object was missing, refused, or simply had nothing
to report, and a notification sent from inside `iter()` arrives once per element.<br>
**Workaround.** Test the prefix, as PennMUSH's own test suite does: `strmatch(%0,#-*)`, or `t()`,
which is false for anything starting `#-`. Code that compares with `=` or `eq()` against exactly
`#-1` needs the prefix test instead. `namelist()` keeps one-word `#-1` and `#-2` entries, because each
entry is a position in a list.

```
> think zone(me)
#-1 NO ZONE SET
> think [strmatch(zone(me),#-*)] [t(zone(me))]
1 0
```

# COMPATIBILITY ARGUMENTS
Functions that accept a different number of arguments here. Every one of these is **additive**: the
call you would write for PennMUSH keeps its PennMUSH meaning, and the extra argument is optional.

`SharpMUSH.Tests/PennMUSH/function-arity.tsv` holds PennMUSH's own declared arity for every
function, and a test compares it against the registered arity on every run, so this list cannot
silently grow. See `docs/guides/registry-parity-and-coverage.md`.

## A trailing `<precision>` on the time functions

**Affects** `convtime() convutctime() csecs() etime() etimefmt() idle() msecs() secs() stringsecs()
timestring() uptime()`.<br>
**PennMUSH** answers whole seconds.<br>
**SharpMUSH** answers whole seconds too, unless you ask for another unit in one more trailing
argument: `s` (the default), `ms`, `f` for fractional.<br>
**Why.** SharpMUSH stores creation and modification times to the millisecond, so `csecs()` truncating
to seconds would make `[num(%0)]:[csecs(%0)]` fail to reconstruct an objid. Rather than change what a
PennMUSH call returns, the finer value is a separate request.<br>
**Workaround.** Omit the argument for PennMUSH behaviour.

```sharp
> think secs()
1789000000
> think secs(ms)
1789000000123
```
(The numbers are whatever the clock says; what matters is that the second is the first with three
more digits.)

## A trailing `<osep>` on the vector functions

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

**PennMUSH** takes at most four arguments and rejects a fifth.<br>
**SharpMUSH** binds every argument past the fourth as a prepared-statement parameter, substituted for
a `?` in the query.<br>
**Why.** The alternative is softcode concatenating values into query text.<br>
**Workaround.** None needed; a four-argument call behaves as PennMUSH's does.

```sharp
> think sql(lit(SELECT name FROM people WHERE id = ?),%b,%b,%b,7)
```

## Argument-less forms PennMUSH does not offer

`config()` with no argument lists every option name. `stext()` with no argument is the current
switch, the same as `stext(0)`. `lwhoid()` accepts the `<status>` argument `lwho()` takes — PennMUSH
declares the pair differently although one C function serves both.

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

```sharp
> think [num(me)]:[csecs(me,ms)]
#1:1789000000123
```
and compare it with `objid(me)`, which is the same two fields.

## Objids and stamped dbrefs

SharpMUSH's `objid` is `<dbref>:<creation time in milliseconds>`. Where PennMUSH's `unparse_dbref`
produces a bare dbref in a message, SharpMUSH may produce the stamped form; the two are not
interchangeable as text. Parse an objid with `num()` when you want the dbref alone. The
representation choice is deliberate and tracked separately in #1006 item 13.

## Number precision

`float_precision` sets how many significant digits floating-point output carries. PennMUSH defaults
to 6; **SharpMUSH defaults to 15**, and clamps the setting to 0–15. Code that formatted PennMUSH
output by relying on its rounding will see more digits here.

**Workaround.** `round()` to the precision you want rather than relying on the default.

```sharp
> think fdiv(1,3)
0.333333333333333
> think round(fdiv(1,3),6)
0.333333
```

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
(the text with < and > escaped to &lt; and &gt;, wrapped in the markup the html format emits)
> think render(ansi(r,red),markup)
red
```

Rendering to a format the game does not otherwise use is how you get a portable string: `html` for a
web page, `ansi` for a terminal capture, neither for plain text.

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

**PennMUSH** is `ctitle(<channel>, <object>)` and `cstatus(<channel>, <object>)`.<br>
**SharpMUSH** is `ctitle(<object>, <channel>)` and `cstatus(<object>, <channel>)` — object first,
consistently across the pair.<br>
**Workaround.** Swap the arguments when importing channel softcode.

```sharp
> think cstatus(me,Public)
ON
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

## `regrabi()`

PennMUSH declares `REGRAB` and `REGRABALL` as taking up to four arguments and `REGRABI` as taking
three. SharpMUSH matches that exactly, including the inconsistency. Whether to give `regrabi()` the
fourth argument is undecided.

# COMPATIBILITY DEFECTS
Differences that are **bugs**, tracked and expected to change. Listed so they are not mistaken for
choices.

  **`hasattr()`, `hasattrp()`, `hasattrval()`, `hasattrpval()` require two arguments.** PennMUSH also
  accepts the single-argument `<object>/<attribute>` form. (#974)<br>
  **`pcreate()` takes no third argument.** PennMUSH accepts an optional dbref to reuse. (#974)<br>
  **`textentries()` is `textentries(<type>[, <osep>])`** — it lists every entry. PennMUSH is
  `textentries(<type>, <pattern>[, <osep>])`, where `<pattern>` is required and filters the list;
  SharpMUSH has no way to filter. (#974)<br>
  **`attrib_set#()` cannot be called.** The parser's function-name token does not admit `#`, so the
  text is returned unchanged. Use `attrib_set()`. (#974)<br>
  **`objmem()` always answers 0.** (#974)<br>
  **Lock creator and flags are lost on import.** A PennMUSH dump's protected, inheritable, visual and
  no_clone locks lose those semantics, and a lock whose creator differs from the object's owner is
  not round-tripped. (#1108)

Movement, queue and economy gaps left by the movement work are enumerated in #1006 rather than
repeated here.

# COMPATIBILITY MATCHED
These once differed and now match PennMUSH; noted here only because earlier SharpMUSH releases
behaved differently.

- Lock operator precedence: `&` binds tighter than `|`, so `a & b | c` is `(a & b) | c`.
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
