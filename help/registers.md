<!-- help-article
{
  "corpus": "help",
  "id": "registers",
  "lookup": "registers",
  "aliases": [],
  "sections": [
    {
      "id": "capture-register-behavior",
      "heading": "Capture register behavior",
      "lookup": "registers capture register behavior"
    }
  ],
  "redirects": {
    "REGISTERS2": "registers capture register behavior"
  }
}
-->
# Registers

A register is essentially a little reserved piece of computer memory that can hold some variable information that you want to pass on to another command. There are thirty registers on the MUSH available via the %0-%9 substitutions and v(0)-v(9) and r(0,args) to r(29,args). There are also many setq registers available via %q- substitution (%q0 - %q9, %qA - %qZ and arbitrary names), or the `r()` function.

The basic registers are filled with information that matches the wildcard pattern of the command trigger. (Before you say "Huh?", here's an example.)

```sharp
    > &COMMAND me=$command *=*:@emit %0 is in register 0 and %1 is in register 1.
    > command whee=blert foo
    whee is in register 0 and blert foo is in register 1.
```

## Capture register behavior

As you can see from the above example, the command trigger had two wildcards separated by a "=" sign. When the user types in the command with some words taking the place of the wildcards, the first register (register 0) is filled with whatever part of the command replaces the first wildcard (in this case, "whee") and the second register is filled with whatever replaces the second ("blert foo").

They can also be filled with information that is passed by an `@trigger` command:

```sharp
    > &SOMECODE me=@emit %0 is in register 0 and %1 is in register 1.
    > @trigger me/somecode=whee,foo bar
  whee is in register 0 and foo bar is in register 1.
```

Please see [setq()] for more information about the setq registers.


::: seealso
- [%]
- [@trigger]
- [$-commands]
- [WILDCARDS]
- [setq()]
- [V()]
- [R()]
- [REGISTERS()]
- [LISTQ()]
- [LETQ()]
- [LISTQ()]
- [STRMATCH()]
:::
