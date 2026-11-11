<!-- help-article
{
  "corpus": "help",
  "id": "pushregs-function",
  "lookup": "pushregs()",
  "aliases": [
    "pushregs"
  ],
  "sections": []
}
-->
# pushregs()

`pushregs(<number>)`<br>
`pushregs(<changes>)`

Saves registers and puts them back later in the same command. It returns nothing.

- A `<number>` other than 0 saves every register that is set. 0 puts every saved register back, then empties the saved copies, so a second 0 empties those registers.
- `<changes>` is a space-separated list. `+<register>` saves one register and `-<register>` puts it back. A register is named by one letter or digit.

For code that should not change the caller's registers at all, [localize()] is simpler. pushregs() comes from RhostMUSH.

```sharp
> think [setq(0,one)][pushregs(1)][setq(0,two)]%q0 [pushregs(0)]%q0
two one
> think [setq(a,x)][pushregs(+a)][setq(a,y)][pushregs(-a)]%qa
x
```

Output: register 0 back to one; register a back to x.

::: seealso
- [setq()]
- [localize()]
- [REGISTERS]
:::
