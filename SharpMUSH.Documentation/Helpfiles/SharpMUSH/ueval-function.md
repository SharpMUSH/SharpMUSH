<!-- help-article
{
  "corpus": "help",
  "id": "ueval-function",
  "lookup": "ueval()",
  "aliases": [
    "ueval",
    "streval()",
    "streval",
    "privilege ceiling"
  ],
  "sections": [
    {
      "id": "levels",
      "heading": "Levels",
      "lookup": "ueval levels"
    }
  ]
}
-->
# ueval() and streval()

`ueval([<object>/]<attribute>/<role>[, <arg0>[, ... , <argN>]])`<br>
`streval(<string>, <role>[, <arg0>[, ... , <argN>]])`

ueval() is u() with a privilege ceiling. The code runs with only what the roles at or below `<role>`'s priority grant, so a wizard can run code as if they were a player. The ceiling covers you, the object the attribute is on and both owners. It only takes permissions away. It never gives control over an object, and a role above your own changes nothing.

streval() evaluates `<string>` under the same ceiling. Its arguments are not evaluated; `<string>` reads them as `%0` onwards.

`<role>` is a role's name or one of RhostMUSH's levels: see [ueval levels].

For ueval() only, the level `subeval` evaluates the attribute's substitutions and calls no function.

These functions come from RhostMUSH. With no `/<role>`, ueval() is `#-1 NO LEVEL SPECIFIED`.

```sharp
> &CHECK me=[controls(%#, *Alice)]
> think u(CHECK) [ueval(CHECK/wizard)] [ueval(CHECK/citizen)]
1 1 0
> think streval(%0,citizen,[add(1,1)])
[add(1,1)]
> &SHOW me=[add(1,2)] %0
> think ueval(SHOW/sub,x)
[add(1,2)] x
```

::: seealso
- [u()]
- [roles]
:::

## Levels

RhostMUSH's levels can be shortened: `citizen`, `guildmaster`, `architect`, `councilor`, `royalty` (a full wizard), `wizard`, `immortal` and `god`. Each level uses the role of the same name when the game has one, as an imported RhostMUSH game does. Otherwise `citizen` and `guildmaster` mean `player`, `architect` means `builder`, `councilor` means `royalty`, and `immortal` means every role but not God's own powers. `god`, or a name that is no role, evaluates with no ceiling.

