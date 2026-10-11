<!-- help-article
{
  "corpus": "help",
  "id": "functions",
  "lookup": "functions",
  "aliases": [
    "FUNCTION"
  ],
  "sections": [
    {
      "id": "brackets",
      "heading": "Brackets",
      "lookup": "functions brackets",
      "aliases": [
        "BRACKETS"
      ]
    },
    {
      "id": "built-in-and-user-functions",
      "heading": "Built-in and user functions",
      "lookup": "functions built-in and user functions"
    }
  ],
  "redirects": {
    "FUNCTIONS2": "functions built-in and user functions"
  }
}
-->
# Functions

Functions are specialized commands used to manipulate strings and other input. Functions take the general form: `FUNCTION(<input>)`

A function evaluates on its own when it starts the text being evaluated: an attribute that u() runs, each argument of another function, and each argument of a command. Brackets, `[FUNCTION(<input>)]`, are needed only for a function that comes after something else, such as text, a %-substitution or another function. See [functions brackets].

Examples:
```sharp
> say first(rest(This is a nice day))
You say, "is"
```
```sharp
> @va me=This is a
> @vb me=nice day
> say first(rest(v(va) [v(vb)]))
You say, "is"
```

In the second example, v(va) starts the argument of rest(), so it needs no brackets; v(vb) comes after it, so it does.

## Brackets

A function is recognised without brackets in these places:

- At the start of an attribute that u(), a lock or an event evaluates: `&NEXT me=add(%0,1)`.
- At the start of each argument a function evaluates: `add(mul(2,3),1)`. A function such as lit() that takes an argument as text evaluates nothing in it: `lit(add(1,2))` gives `add(1,2)`.
- At the start of each argument of a command: `think add(1,2)`, `@pemit %#=u(me/SHOW)`.

Spaces after the command name, `=`, `(` and `,` are skipped. Text after a leading function is still evaluated, so only a later function needs brackets.

```sharp
> think add(1,2) apples
3 apples
> think Total: add(1,2)
Total: add(1,2)
> think Total: [add(1,2)]
Total: 3
> think add(1,2)[mul(2,3)]
36
> think iter(1 2,%i0: [add(%i0,1)])
1: 2 2: 3
```

Brackets around a whole attribute, a whole evaluated argument or a %-substitution change nothing, and each pair is one more step to evaluate, so leave them out there: `if(%0,u(me/YES),u(me/NO))`, not `[if(%0,[u(me/YES)],[u(me/NO)])]`.

A function after text inside an argument is not just left unevaluated: its own commas and closing paren split and close the outer function. `think cat(a,b add(1,2))` gives `a b add(1 2)`. That is with paren_groups off, the default; see [compatibility config].

A word that is a function's name, with `(` straight after it at the start, is a call even when it was meant as text: `think Last(minute) changes are fine` gives `minute changes are fine`. Write `\Last(minute)`. A name that is not a function stays text outside brackets and is an error inside them.

## Built-in and user functions

There are two types of functions, "built-in functions" and "global user functions", also known as "@functions". You can get a complete list of functions on this game with "@list/functions".

Built-in functions are written in the game hardcode, while @functions are written in softcode, and then made global with the "@function" command. Both are used in exactly the same manner. For more information on @functions, see [@function].


::: seealso
- [mushcode]
- [FUNCTION LIST]
:::
