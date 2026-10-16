<!-- help-article
{
  "corpus": "help",
  "id": "switch-command",
  "lookup": "@switch",
  "aliases": [
    "@sw",
    "@select"
  ],
  "sections": [
    {
      "id": "tokens",
      "heading": "The #$ token",
      "lookup": "@switch tokens"
    },
    {
      "id": "switches",
      "heading": "Switches",
      "lookup": "@switch switches"
    },
    {
      "id": "inline-execution",
      "heading": "Inline execution",
      "lookup": "@switch inline execution"
    },
    {
      "id": "pattern-matching-examples",
      "heading": "Pattern matching examples",
      "lookup": "@switch pattern matching examples"
    },
    {
      "id": "queue-ordering-examples",
      "heading": "Queue ordering examples",
      "lookup": "@switch queue ordering examples"
    }
  ],
  "redirects": {
    "@switch2": "@switch inline execution",
    "@switch3": "@switch pattern matching examples",
    "@switch4": "@switch queue ordering examples"
  }
}
-->
# @switch

`@switch[/<switch>] <string>=<expr1>, <action1> [,<exprN>, <actionN>]... [,<default>]`<br>
`@select <string>=<expr1>, <action1> [,<exprN>, <actionN>]... [,<default>]`

For those of you familiar with programming, these command acts like if/then/else or switch/case. It compares `<string>` against whatever each `<expr>` evaluates to. If `<string>` and `<expr>` match, the action list associated with that `<expr>` is carried out. If no match is found, the `<default>` action list is carried out. @switch runs `<action>`s for all matching `<expr>`s by default, while @select only runs the `<action>` for the first matching `<expr>`.

Output: with `/inline` or `/inplace`, the output of the last command run; queued, nothing. See [command output].

If `<expr>` is a regexp or a wildcard glob, then $0-$9 will be set with capture data. (In wildcard globbing, every wildcard captures.)

- [@switch tokens]: what #$ stands for, and in nested switches
- [@switch switches]: all or first match, notify, inline and regexp

## The #$ token

In an `<action>` or the `<default>`, "#$" stands for the evaluated `<string>`, and a queued `<action>` keeps it. The value is never evaluated as code. In nested switches #$ refers to the outermost one whose `<action>` it is written in; for the current one, use %$0 or stext(0). Like switch()'s, #$ counts only in the `<action>` as written: in an `<expr>`, in the code of an attribute the `<action>` triggers or calls, and inside lit() or after \, it is plain text. With the `innermost_tokens` option on, #$ names the innermost switch instead, wherever it is written. See [compatibility config innermost loop tokens].

## Switches

`@switch/all` runs `<action>`s for all matching `<expr>`s. Default for @switch.<br>
`@switch/first` runs `<action>` for the first matching `<expr>` only. Same as @select, and often the desired behaviour.<br>
`@switch/notify` queues "@notify me" after the last `<action>`.<br>
`@switch/inline` runs all actions in place, instead of creating a new queue entry for them.<br>
`@switch/regexp` makes `<expr>`s case-insensitive regular expressions, not wildcard/glob patterns.
`@switch/regall` is `@switch/all/regexp`, and `@switch/regfirst` is `@switch/first/regexp`. Both spellings come from RhostMUSH.

## Inline execution

When using `@switch/inline`, an @break in an `<action>` will stop the calling action list (and any further `<action>`s) from running. Each `<action>` will also be able to see/alter the q-registers for the calling action list. The following switches can be used with `/inline` to alter this behaviour:
- /nobreak: @breaks in `<action>` do not effect to the calling action list
- /localize: q-registers are saved before each `<action>` is run, and restored after it completes
- /clearregs: q-registers are all reset before each `<action>` is run. Most useful when used in combination with /localize. `/clearreg`, RhostMUSH's spelling, is the same switch.

`@switch/inplace` is an alias for `@switch/inline/nobreak/localize`.

::: seealso
- [SWITCH WILDCARDS]
- [switch()]
- [@if]
- [@break]
- [STEXT()]
- [STEXT()]
:::

## Pattern matching examples

```sharp
> &SWITCH_EX thing=$foo *: @switch %0=*a*, :acks, *b*, :bars, :glurps
> foo abc
thing acks
thing bars
> foo xxx
thing glurps
```

```sharp
> &SWITCH_EX thing=$foo *: @switch/first %0=*a*, :acks,*b*, :bars, :glurps
> foo abc
thing acks
```

```sharp
> &SWITCH_EX thing=$test: @switch hasflag(%#,PUPPET)=1, say Puppet!, say Not Puppet!
> test
thing says, "Not Puppet!"
```

```sharp
> &SWITCH_EX thing=$foo *: @switch %0=*a*,say Before: '$0'. After: '$1'
> foo foobarbaz
thing says, "Before: 'foob'. After: 'rbaz'
```

## Queue ordering examples

```sharp
> &SWITCH_EX me=$foo *:think before ; @switch %0=1,think one ; think after
> foo 1
thing before
thing after
thing one
```

```sharp
> &SWITCH_EX me=$foo *:think before ; @switch/inline %0=1,think one ; think after
> foo 1
thing before
thing one
thing after
```
