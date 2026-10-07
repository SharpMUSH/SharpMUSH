<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-matched",
  "lookup": "compatibility matched",
  "aliases": [],
  "sections": [
    {
      "id": "known-limitations",
      "heading": "Known limitations",
      "lookup": "compatibility matched known limitations"
    },
    {
      "id": "see-also",
      "heading": "See also",
      "lookup": "compatibility matched see also"
    }
  ]
}
-->
# Compatibility Matched

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
- A HALTED object runs none of its softcode: `u()`/`ufun()` return the stored attribute text
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
- A QUIET player, or a player writing to a QUIET object they own, was once still told
  `<object>/<attr> - Set.` by `&`, `@desc` and the other attribute commands, and `Parent changed.`,
  `Home set.`, `@edit`'s `- Set.` and `@power`'s `granted.`; `@notify` and `@drain` now say nothing
  for a QUIET executor, and `@drain` says `Drained.` otherwise. See `help QUIET`.
- A queue entry that runs out of `queue_entry_cpu_time` once sent
  `#-1 EXECUTION TIME LIMIT EXCEEDED` to every connection of its owner; its enactor now hears
  `CPU usage exceeded.`, unless QUIET.
- `@chownall` once said `Changed ownership of N object(s) from A to B.`; it now says
  `Ownership changed for N objects.`
- A new game once started with `function_recursion_limit 100`, `function_invocation_limit 100000`
  and `max_named_qregs 100`; it now starts with PennMUSH's 50, 25000 and 50.

## Known limitations

Not yet at parity; may change in a future release.

- **`%c` and `%u` inside `@trigger` and locks.** `@trigger` runs its list in place, so that list
  starts from the triggering command's `%c`/`%u` instead of empty ones. A lock evaluated for a
  `$`-command sees neither. `%u` also shows the value of `&attr obj=value` and `@attr obj=value` as
  written, where PennMUSH shows it evaluated in a queued list, and a computed `&` attribute name as
  written (`ATTRIB_SET/[CAT(F,OO)]`), where PennMUSH shows the name it evaluated to
  (`ATTRIB_SET/FOO`).
- **`@trigger` and `@force` count toward the in-place depth.** Both run their list in place rather
  than queueing it, so it is one more of the 50 in-place levels a queue entry may nest (see
  `help @include`). An attribute that `@trigger`s itself stops after 50 rounds, where PennMUSH
  queues each round as a new entry and goes on.
- **Characters above U+FFFF** (emoji and other supplementary-plane characters) are stored as UTF-16
  surrogate pairs. This is internally consistent, but a substitution or slice that lands between the
  two halves of a pair could split it. Rare in practice.

## See also
- `help @halt`
- `help iter()`
- `help substitutions`
- `help exception`
