<!-- help-article
{
  "corpus": "help",
  "id": "u2local-function",
  "lookup": "u2local()",
  "aliases": [
    "U2()",
    "u2local"
  ],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "u2local examples"}
  ]
}
-->
# u2local()

`u2([<object>/]<attribute>[, <arg0>[, ... , <arg29>]])`<br>
`u2local([<object>/]<attribute>[, <arg0>[, ... , <arg29>]])`

u2() evaluates an attribute the way u() does, with one difference: the code runs as the object calling u2(), not as the object the attribute is on. Inside it, `me`, %! and %@ are the caller, so the attribute works on the caller's behalf and with the caller's permissions. `<object>` is found, and the attribute checked, exactly as for u(); a missing attribute gives nothing. The arguments are passed as %0 onward.

u2local() is to u2() what ulocal() is to u(): it runs the attribute as the calling object, like u2(), and keeps the q-registers safe while it runs, like ulocal(). Whatever the attribute does to the registers is undone when it finishes.

- [u2local examples]: u() beside u2(), and registers kept by u2local()

u2() and u2local() come from RhostMUSH and take the same arguments. RhostMUSH's u() runs as the attribute's object in its usual build, as here, and u2() keeps its older behavior of running as the caller.

::: seealso
- [u()]
- [ULOCAL()]
- [u2default()]
- [LOCALIZE()]
- [privatize()]
:::

## Examples

```sharp
> &SHOW_ME #123=[name(me)]
> think u(#123/SHOW_ME)
Widget
> think u2(#123/SHOW_ME)
Wizard
> &SET_INNER #123=[setq(0,inner)]done
> think [setq(0,outer)][u2local(#123/SET_INNER)] %q0
done outer
> think [setq(0,outer)][u2(#123/SET_INNER)] %q0
done inner
```
