# PENNMUSH COMPATIBILITY
SharpMUSH targets PennMUSH softcode compatibility, but a few behaviors differ on
purpose, and a few are known limitations. This topic lists them so you are not
surprised when code imported from PennMUSH behaves differently.

## Boolean compatibility

Boolean conditions use the same rules in functions, command guards, list filters,
and indirect calls. With `tiny_booleans` off, numeric zero (including `-0`, `0.0`
and hexadecimal zero), an empty string, space-only text, and values beginning
`#-` are false. Other text is true, including the word `false`. A preserved tab
is text, and a trailing space makes an otherwise numeric value text.

With `tiny_booleans` on, a leading signed integer determines truth. Thus `text`
and `0.1` are false, while `1.2text` is true. The compatibility conversion uses
a signed 64-bit prefix, saturates overflow, then takes its low 32 bits. For
example, `4294967296` is false. The setting is read at evaluation time.

`neq()` is the numeric inverse of `eq()`: it is true when not all arguments are
equal. `condall()` returns every result with a true condition; `ncond()` and
`ncondall()` select false conditions. A condition is a single value, not a list
of separate booleans. A selected empty result does not cause the default to run.

## Numeric compatibility

Arithmetic and numeric comparisons read `tiny_math` and `null_eq_zero` at call
time, including indirect `#apply` calls. With `tiny_math` off, the complete
argument must be a number; an empty argument is zero only when `null_eq_zero`
is on. Scientific notation is accepted. Real-valued arguments also accept
hexadecimal notation, such as `0x10` and `-0x1.8p2` (16 and -6).

With `tiny_math` on, the leading numeric prefix is used, and text without one
becomes zero: `add(12foo,2)` returns 14 and `add(foo,2)` returns 2. Integer
arithmetic takes an integer prefix, so `div(1.5,1)` returns 1. Each function
retains its existing signed, unsigned, decimal or floating-point range; this
setting does not remove range checks or give decimal arithmetic support for
infinity. Numeric input and output use a decimal point regardless of the
server's language settings.

`inc()` and `dec()` adjust a signed integer suffix. Without a suffix, they
append 1 or -1 only when `null_eq_zero` is on; otherwise they report an integer
suffix error. Overflow reports `#-1 OUT OF RANGE`. `tiny_math` does not change
these string-counter rules.

## Trim argument order

With `tiny_trim_fun` off, `trim(text,characters,side)` uses Penn argument order.
With it on, `trim(text,side,characters)` uses Tiny argument order. The default
characters are spaces and the default side is both; `l` and `r` select the left
or right side. The setting is read when the function runs.

Use `trimpenn(text,characters,side)` or `trimtiny(text,side,characters)` when a
package needs a fixed argument order regardless of game configuration.

## Intentional divergences

These are deliberate. SharpMUSH does not intend to change them to match PennMUSH.

**The command word is not evaluated for built-in dispatch.** PennMUSH evaluates
the first word of a command line before matching it, so `[strcat(th,ink)] hello`
runs `think`. SharpMUSH matches the built-in command word literally. Build
command names as literal text. (This is specifically about matching a built-in
command name. It does not contradict the `$`-command rule below: once built-in
dispatch has not matched, `$`-commands *are* matched against the fully evaluated
line — that is a separate, later stage.)

**Malformed expressions are an error, not silent text.** PennMUSH evaluates
almost anything — an unclosed `[`, `(`, or `{`, or a trailing `\`, still
produces output. SharpMUSH is stricter: an unbalanced expression returns
`#-1 PARSER FAILURE`. Balance your brackets. (Orphaned closers with no opener,
such as a stray `]`, are still treated as literal text, as in PennMUSH.)

**Function arguments are not evaluated before an argument-count error.** In
PennMUSH `not(setq(0,x),1)` sets `%q0` and then reports the arity error;
its arguments run for their side effects first. SharpMUSH validates the
argument count first, so those side effects do not happen. Do not rely on side
effects in the arguments of a miscalled function.

**Evaluation stops at the first limit.** When a function invocation, recursion,
call-depth, or output limit is hit, SharpMUSH stops evaluating the rest of the
expression. PennMUSH continues past the error. Expect no output after the point
where a limit is reached.

**`##` in `iter()` is data, not code.** PennMUSH splices each list element into
the pattern as text and then evaluates it, so element text containing softcode
executes — an injection risk. SharpMUSH binds `##` to the iteration register
(equivalent to `%iL`) and does not re-evaluate element text. `iter(list,##)`
substitutes the element; it does not run it. Code that relied on PennMUSH
splice-then-evaluate (e.g. storing softcode in a list and running it via `##`)
will not work — use `u()` or an explicit evaluation instead.

**Function names cannot be produced by evaluation.** PennMUSH builds the called
function's name from evaluated output, so `[setq(0,add)]%q0(1,2)` calls `add`.
SharpMUSH recognizes function names lexically, so a name that only appears after
substitution is treated as ordinary text, not a call.

**A command that crashes says so.** PennMUSH has no equivalent: when an internal
error escapes a SharpMUSH command, the command returns
`#-1 EXCEPTION: <json>` and the player is notified with the same text, instead of
producing no output at all. See `help exception` for the payload and what a
mortal versus a wizard is shown.

**Unescaped commas are never absorbed by a final argument.** PennMUSH lets the
last argument of functions such as `pemit()`, `emit()`, and `capstr()` swallow
extra unescaped commas — `capstr(a,b,c)` capitalizes the string `a,b,c` — though
it now warns that this is deprecated. SharpMUSH does not do this: every comma is
an argument separator, so `capstr(a,b,c)` is a too-many-arguments error. Escape
the commas (`\,`) or wrap the text in braces (`capstr({a,b,c})`). This is
deliberate — it avoids silently changing what counts as an argument.

## Behaviors that match PennMUSH

These once differed and now match PennMUSH; noted here only because earlier
SharpMUSH releases behaved differently.

- Lock operator precedence: `&` binds tighter than `|`, so `a & b | c` is
  `(a & b) | c`.
- `letq()` requires an odd number of arguments and `setr()` an even number;
  `case()`/`caseall()` have no parity requirement.
- An unknown function name outside `[...]` is left as literal text
  (`think foo(bar)` prints `foo(bar)`); inside `[...]` it is
  `#-1 FUNCTION (FOO) NOT FOUND`.
- A HALTED object runs none of its softcode — `u()`/`ufun()` return the stored
  attribute text *unevaluated* (PennMUSH's `PE_NOTHING`: `u()` of a halted
  object's `[add(1,2)]` yields the literal `[add(1,2)]`), while its `$`-commands
  do not fire. `@halt <object>` and `@chown` (which halts to break ownership
  loops) rely on this.
- An uppercase substitution selector capitalizes the first output character:
  `%Q0`, `%N`, `%I0`, `%S` capitalize; `%q0`, `%n`, `%i0`, `%s` do not.
- `% ` (a percent followed by a space) is emitted literally as `% `.
- `$`-commands are matched against the command line *after* it is evaluated, so a
  command whose name or arguments only appear once substitutions and functions
  run still matches and captures its `%0..` from the evaluated text.
- An unknown function inside `[...]` whose name is close to a real one is
  reported as `#-1 FUNCTION (NAME) NOT FOUND DID YOU MEAN 'CLOSEST'`.

## Known limitations

Not yet at parity; may change in a future release.

- **`%u` equals `%c`.** Both currently return the command *before* evaluation.
  In PennMUSH `%c` is the raw command and `%u` is the command after argument
  evaluation.
- **Characters above U+FFFF** (emoji and other supplementary-plane characters)
  are stored as UTF-16 surrogate pairs. This is internally consistent, but a
  substitution or slice that lands between the two halves of a pair could split
  it. Rare in practice.

## See also
- `help @halt`
- `help iter()`
- `help substitutions`
