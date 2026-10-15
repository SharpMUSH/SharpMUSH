<!-- help-article
{
  "corpus": "help",
  "id": "nameq-function",
  "lookup": "nameq()",
  "aliases": [
    "nameq"
  ],
  "sections": [
    {
      "id": "listing-registers",
      "heading": "Listing registers",
      "lookup": "nameq listing registers"
    }
  ]
}
-->
# nameq()

`nameq(<register>)`<br>
`nameq(<register>, <label>)`<br>
`nameq(<label>, <ignored>, <number>)`<br>
`nameq(<pattern>, <ignored>, <type>)`

A register can carry a label, a second name set by setq()'s third argument or by nameq(). nameq() reads, sets and searches those labels.

With one argument, nameq() returns the label of `<register>`, or nothing when it has none. Given a label, it returns that label.

With two arguments, it gives `<register>` the label `<label>`, replacing any label it had. An empty `<label>` removes the register's label. A one-character label does nothing, a label that is a number or holds a `>` is `#-1 INVALID LABEL`, and a label another register already has is `#-1 LABEL [<label>] ALREADY IN USE`. nameq() never changes a register's value.

With three arguments the second is ignored; leave it empty. With a number other than 0 as the third argument, nameq() returns the name of the register `<label>` belongs to. Given a register name instead, it returns the label.

Output: a label, a register name, or nothing.

nameq() comes from RhostMUSH. RhostMUSH's `nameq()` with an empty first argument returns how many registers it has; here registers are named freely, so that is an error.

```sharp
> think [setq(0, 42, total)][nameq(0)] [nameq(total)] [nameq(total, , 1)]
total total 0
> think [nameq(1, count)][setq(count, 7)]%q1
7
> think [setq(0, a, total)][nameq(1, total)]
#-1 LABEL [total] ALREADY IN USE
```

::: seealso
- [setq()]
- [setqm()]
- [r()]
- [registers]
:::

## Listing registers

With a letter as the third argument, nameq() lists the registers that match the wildcard `<pattern>`, separated by spaces, in the order listq() uses. The letter says what the pattern is matched against:
- `l`: the label.
- `n`: the register's name, among registers holding a value.
- `v`: the value.
- `u`: the label or the value.
- `b`: the label or the value, among registers with both.
- `f`: the label or the name, among registers with both.
- `a`: the label, the value or the name, among registers with both.

Capital letters mean the same; in RhostMUSH they choose how its lettered registers are shown, which named registers do not need.

```sharp
> think [setq(0, 42, total)][setq(1, 3, count)][setq(tax, 7)][nameq(*, , l)]
0 1
> think [setq(0, 42, total)][setq(tax, 7)][nameq(t*, , u)] / [nameq(4*, , v)]
0 / 0
```
