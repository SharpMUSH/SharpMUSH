<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-parser",
  "lookup": "compatibility parser",
  "aliases": [],
  "sections": [
    {
      "id": "the-command-word-is-not-evaluated-for-built-in-dispatch",
      "heading": "The command word is not evaluated for built-in dispatch",
      "lookup": "compatibility parser the command word is not evaluated for built in dispatch"
    },
    {
      "id": "malformed-expressions-are-an-error-not-silent-text",
      "heading": "Malformed expressions are an error, not silent text",
      "lookup": "compatibility parser malformed expressions are an error not silent text"
    },
    {
      "id": "function-arguments-are-not-evaluated-before-an-argument-count-error",
      "heading": "Function arguments are not evaluated before an argument-count error",
      "lookup": "compatibility parser function arguments are not evaluated before an argument count error"
    },
    {
      "id": "evaluation-stops-at-the-first-limit",
      "heading": "Evaluation stops at the first limit",
      "lookup": "compatibility parser evaluation stops at the first limit"
    },
    {
      "id": "call-limit-counts-calls-not-parser-recursion",
      "heading": "`call_limit` counts calls, not parser recursion",
      "lookup": "compatibility parser call limit counts calls not parser recursion"
    },
    {
      "id": "in-iter-is-data-not-code",
      "heading": "`##` in `iter()` is data, not code",
      "lookup": "compatibility parser in iter is data not code"
    },
    {
      "id": "function-names-cannot-be-produced-by-evaluation",
      "heading": "Function names cannot be produced by evaluation",
      "lookup": "compatibility parser function names cannot be produced by evaluation"
    },
    {
      "id": "a-literal-tab-after-or-is-layout",
      "heading": "A literal tab after `(` or `,` is layout",
      "lookup": "compatibility parser a literal tab after or is layout"
    },
    {
      "id": "unescaped-commas-are-never-absorbed-by-a-final-argument",
      "heading": "Unescaped commas are never absorbed by a final argument",
      "lookup": "compatibility parser unescaped commas are never absorbed by a final argument"
    },
    {
      "id": "a-command-that-crashes-says-so",
      "heading": "A command that crashes says so",
      "lookup": "compatibility parser a command that crashes says so"
    },
    {
      "id": "a-failing-function-says-why",
      "heading": "A failing function says why",
      "lookup": "compatibility parser a failing function says why"
    },
    {
      "id": "a-set-function-s-delimiter-can-be-longer-than-one-character",
      "heading": "A set function's delimiter can be longer than one character",
      "lookup": "compatibility parser a set function s delimiter can be longer than one character"
    },
    {
      "id": "a-wizard-s-mailstats-is-their-own",
      "heading": "A Wizard's `mailstats()` is their own",
      "lookup": "compatibility parser a wizard s mailstats is their own"
    },
    {
      "id": "is-the-last-command-s-output",
      "heading": "`%>` is the last command's output",
      "lookup": "compatibility parser is the last command s output"
    },
    {
      "id": "and-pipe-a-command-s-output",
      "heading": "`;|` and `%|` pipe a command's output",
      "lookup": "compatibility parser and pipe a command s output"
    }
  ]
}
-->
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
rule in [compatibility matched]: once built-in dispatch has not matched, `$`-commands *are* matched
against the fully evaluated line (a separate, later stage).

## Malformed expressions are an error, not silent text

**A choice.**

**PennMUSH** evaluates almost anything: an unclosed `[`, `(`, or `{`, or a trailing `\` still
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

## `call_limit` counts calls, not parser recursion

**A choice.**

**PennMUSH** counts every recursive call of its expression parser against `call_limit` (each
bracket, function argument and attribute body, `src/parse.c:2128`) and ships it at 100.<br>
**SharpMUSH** counts function calls and attribute evaluations, about a third as many for the same
code, and starts a new game at 1000.<br>
**Why.** At 100 the call limit would trip before `function_recursion_limit` (50) on ordinary
self-recursion, so `&X me=[u(me/X)]` would report `CALL LIMIT EXCEEDED` where PennMUSH reports
`FUNCTION RECURSION LIMIT EXCEEDED`. 1000 keeps the recursion limit the one that answers.<br>
**Workaround.** None needed. A game that wants a tighter bound can lower it with `@config/set`.

PennMUSH answers `100`:

```sharp
> think config(call_limit)
1000
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
extra unescaped commas (`capstr(a,b,c)` capitalises the string `a,b,c`), though it now warns that
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
[compatibility arguments]), a PennMUSH-era call with an unescaped comma does not fail: it passes that
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

## `%>` is the last command's output

**A choice.**

**PennMUSH** has no `%>` substitution, so it evaluates `%>` to a plain `>`.<br>
**SharpMUSH** substitutes the output of the last command run in the same queue entry: after
`@dig Kitchen`, `%>` is the new room's dbref. A typed line starts with it empty. See
`help command output`.<br>
**Why.** It lets one command's result feed the next without a search or a register.<br>
**Workaround.** Write a plain `>`, which means the same on both servers. Importing a
PennMUSH database names every attribute that uses `%>`.

PennMUSH answers `a>b` to the first line:

```sharp
> think a%>b
ab
> think a>b
a>b
```

## `;|` and `%|` pipe a command's output

**A choice.**

**PennMUSH** has no command piping. It evaluates `%|` to a plain `|`, and runs `;|` as a `;`
followed by a command that starts with `|` (`; |` too).<br>
**SharpMUSH** pipes as TinyMUX does: in an action list, a command followed by `;|` has what it
shows you passed to the next command, which reads it as `%|`. Everywhere else `%|` is empty. See
`help piping`.<br>
**Why.** It lets softcode use what a command shows, which has no function of its own (`look`),
and TinyMUX code that pipes runs unchanged.<br>
**Workaround.** Write a plain `|`, which means the same on both servers. No command starts with `|`
on either server, so `;|` had no use in PennMUSH. Importing a PennMUSH database names every
attribute that uses `%|` or `;|`.

PennMUSH answers `a|b` to the first line:

```sharp
> think a%|b
ab
> think a|b
a|b
```
