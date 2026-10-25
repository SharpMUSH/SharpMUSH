<!-- help-article
{
  "corpus": "help",
  "id": "setq-function",
  "lookup": "setq()",
  "aliases": [
    "SETR()",
    "setq"
  ],
  "sections": [
    {
      "id": "reading-registers",
      "heading": "Reading registers",
      "lookup": "setq reading registers"
    },
    {
      "id": "register-names-and-limits",
      "heading": "Register names and limits",
      "lookup": "setq register names and limits"
    },
    {
      "id": "expression-examples",
      "heading": "Expression examples",
      "lookup": "setq expression examples"
    },
    {
      "id": "command-thread-lifetime",
      "heading": "Command thread lifetime",
      "lookup": "setq command thread lifetime"
    },
    {
      "id": "register-labels",
      "heading": "Register labels",
      "lookup": "setq register labels"
    },
    {
      "id": "label-examples",
      "heading": "Label examples",
      "lookup": "setq label examples"
    }
  ],
  "redirects": {
    "SETQ2": "setq register names and limits",
    "SETQ3": "setq expression examples",
    "SETQ4": "setq command thread lifetime"
  }
}
-->
# setq()

`setq(<register1>, <string1>[, ... , <registerN>, <stringN>])`<br>
`setr(<register1>, <string1>[, ... , <registerN>, <stringN>])`<br>
`setq(<register>, <string>, <label>)`<br>
`setr(<register>, <string>, <label>)`

The setq() and setr() functions are used to copy strings into local registers assigned arbitrary names (Much like variables in other programming languages.) setq() returns a null string; it is a purely "side effect" function. setr() returns the value stored. Multiple registers can be assigned with a single setq() or setr(), with additional pairs of registers and values in the function's arguments. In this case, setr() returns the value stored in the first register listed. All arguments are evaluated before any registers are set; to use a register set earlier in the same call, use setqm() or setrm(), which set them in order.

With exactly three arguments, the third is a label: another name for the register (see [setq register labels]).

::: seealso
- [setqm()]
- [R()]
- [LISTQ()]
- [LETQ()]
- [LOCALIZE()]
- [ulocal()]
- [REGISTERS()]
- [nameq()]
:::

## Reading registers

Registers set via setq() or setr() can be accessed via the r() function. Single-character registers can also be accessed via the %qN substitution, and ones with longer names via `%q<NAME>` (Note that the <>'s are required.) Attempting to access a register that hasn't been set results in an empty string.

Register names are case insensitive: setq(A, foo) and setq(a, foo) both set the same register, and %qA and %qa both fetch its value.

## Register names and limits

Register names may contain letters, digits, "_", "." and "-". A name with any other character makes setq() return "#-1 REGISTER NAME INVALID".

There is no limit on how many named registers you may set in a queue entry.

## Expression examples

The setq() function is probably best used at the start of the string being manipulated, such as in the following example:

```sharp
> &TEST object=strlen(%0)
> &CMD object=$test *: say setq(0,u(TEST,%0))Test. %0 has length %q0.
> test Foo
Object says, "Test. Foo has length 3."
```

In this case, it is a waste to use setq(), since we only use the function result once, but if TEST was a complex function being used multiple times within the same command, it would be much more efficient to use the local register, since TEST would then only be evaluated once. setq() can thus be used to improve the readability of MUSH code, as well as to cut down the amount of time needed to do complex evaluations.

Swapping the contents of registers can be done without writing to temporary registers by setting both registers at once, so the code:

```sharp
> think setq(0,foo,one,bar)%q0%q<one> - [setq(0,r(one),one,%q0)]%q0%q<one>
foobar - barfoo
```

## Command thread lifetime

The registers set by setq() can be used in later commands in the same thread. That is, the registers are set to null on all $-commands, ^-commands, A-attribute triggers, etc., but are then retained from that point forward through the execution of all your code. Code branches like @wait and @switch retain the register values from the time of the branch.

Example:
```sharp
say setr(what,foo); @wait 0=say %q<what>; say setr(what,bar)
Object says "foo"
Object says "bar"
Object says "foo"
```

## Register labels

`setq(<register>, <string>, <label>)` sets the register and gives it `<label>` as a second name. From then on `%q<label>`, `r(label)` and `setq(label, ...)` read and write that same register, until the register is given another label or the registers are cleared. A register has one label at a time, and a label names one register: giving it to a second register takes it from the first. Labels are saved and restored with the registers by localize(), ulocal() and the `/localize` switches.

A label is at least two characters long, is not a number and has no `>` in it. With a third argument that is not a label, the call is PennMUSH's odd-argument error; `nameq()` answers `#-1 INVALID LABEL` instead. The register may also be `+` (the register the label already names, else the first of 0-9 and A-Z with no label and no value) or `!` (the same, but a register that holds a value may be taken).

nameq() reads and changes labels, and setqm() takes `<register>><label>` to do the same in order.

## Label examples

Labels come from RhostMUSH. One difference: RhostMUSH lets two registers carry the same label, and the label then reads the first of them.

```sharp
> think [setq(0, 15, damage)]%q<damage> %q0
15 15
> think [setq(0, 15, damage)][setq(damage, 20)]%q0
20
> think [setq(+, north, exit)][nameq(exit, , 1)]: %q<exit>
0: north
```
