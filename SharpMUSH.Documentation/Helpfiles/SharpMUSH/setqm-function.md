<!-- help-article
{
  "corpus": "help",
  "id": "setqm-function",
  "lookup": "setqm()",
  "aliases": [
    "SETRM()",
    "setqm",
    "setrm"
  ],
  "sections": [
    {
      "id": "returned-values",
      "heading": "Returned values",
      "lookup": "setrm returned values"
    }
  ]
}
-->
# setqm()

`setqm(<register1>, <value1>[, ... , <registerN>, <valueN>])`<br>
`setrm(<register1>, <value1>[, ... , <registerN>, <valueN>][, <delimiter>])`

setqm() and setrm() set several registers in one call, in order: each pair is evaluated only after the pair before it has been set, so a value can use a register set earlier in the same call. setq() and setr() evaluate every argument before setting any register, so they cannot.

setqm() returns nothing. setrm() returns every value it set (see 'help setrm returned values').

Register names follow the same rules as in setq(), and a name is evaluated too, so it can be built from a register set before it. If any name is not a valid register name, the other pairs are still set and the function returns `#-1 REGISTER NAME INVALID`.

Examples:
```sharp
> think [setqm(cmd_count, 3, cmd_total, mul(%q<cmd_count>, 2))]%q<cmd_total>
6
> think [setq(cmd_count, 3, cmd_total, mul(%q<cmd_count>, 2))]%q<cmd_total>
#-1 ARGUMENTS MUST BE NUMBERS
```

In the second line, setq() works out `mul(%q<cmd_count>, 2)` before cmd_count is set, so mul() is given an empty register.

Building a register name from one set earlier in the same call:
```sharp
> think [setqm(cmd_section, list, cmd_%q<cmd_section>_switches, all unread)]%q<cmd_list_switches>
all unread
```

setqm() and setrm() come from RhostMUSH and take the same arguments, except RhostMUSH's `<register>><label>` form.

::: seealso
- [setq()]
- [LETQ()]
- [R()]
- [LISTQ()]
- [UNSETQ()]
:::

## Returned values

setrm() returns the values it set, in order, separated by a space. An odd number of arguments makes the last one a delimiter, which may be empty. Three arguments is an error, since one value needs no delimiter.

```sharp
> think setrm(page_first, 1, page_last, add(%q<page_first>, 19))
1 20
> think setrm(page_first, 1, page_last, add(%q<page_first>, 19), -)
1-20
> think setrm(word_one, foo, word_two, %q<word_one>bar, )
foofoobar
```
