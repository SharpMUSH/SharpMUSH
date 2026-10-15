<!-- help-article
{
  "corpus": "help",
  "id": "roll-data",
  "lookup": "roll data",
  "aliases": [],
  "sections": [
    {
      "id": "roll-data-names",
      "heading": "Names",
      "lookup": "roll names"
    },
    {
      "id": "roll-data-attributes",
      "heading": "Attributes",
      "lookup": "roll attributes"
    },
    {
      "id": "roll-data-json",
      "heading": "JSON",
      "lookup": "roll json data"
    }
  ]
}
-->
# Roll Data

`<data>`, the second argument to roll(), holds the values a formula names: an object's attributes, or a JSON object.

## Names

A name in the formula is looked up in `<data>`. A number is used as it is. Anything else is itself a formula, so a value can roll dice and name other values. A name may start with `@`, and a dot steps down a level.

Every name, however deep, is looked up from the top of `<data>`. Each use of a name rolls it again. A value that leads back to itself is an error rather than a roll that never ends. Names that look like dice, such as `d6`, are read as dice.

## Attributes

When `<data>` is an object, a name is one of its attributes and a dot is a backtick: `@stat.dex` reads `STAT`DEX`. Attributes are found and checked the way get() finds and checks them, and are never evaluated: what is stored is the number or formula.

```sharp
> &stat`dex me=1d6+@stat.speed
> &stat`speed me=2
> think roll(1dF+@stat.dex,me,text)
1dF[1] + stat.dex(1d6[4] + stat.speed(2)) = 7
```

Output: each name is followed by what it came to, in brackets. DEX rolled 1d6 and added SPEED.

## JSON

When `<data>` starts with `{`, it is a JSON object. A name is matched exactly first, then ignoring case. true counts as 1 and false as 0.

```sharp
> &stats me={"DEXTERITY":"1d6+SPEED","SPEED":2,"skills":{"stealth":3}}
> think roll(1dF+DEXTERITY+skills.stealth,v(stats),text)
1dF[-1] + DEXTERITY(1d6[5] + SPEED(2)) + skills.stealth(3) = 9
```

::: seealso
- [roll()]
- [ROLL DICE]
- [get()]
- [json()]
:::
