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
`setr(<register1>, <string1>[, ... , <registerN>, <stringN>])`

The setq() and setr() functions are used to copy strings into local registers assigned arbitrary names (Much like variables in other programming languages.) setq() returns a null string; it is a purely "side effect" function. setr() returns the value stored. Multiple registers can be assigned with a single setq() or setr(), with additional pairs of registers and values in the function's arguments. In this case, setr() returns the value stored in the first register listed. All arguments are evaluated before any registers are set; if you want to use the result of setting one register in setting another, use multiple setq()s.

Registers set via setq() or setr() can be accessed via the r() function. Single-character registers can also be accessed via the %qN substitution, and ones with longer names via %q`<NAME>` (Note that the <>'s are required.) Attempting to access a register that hasn't been set results in an empty string.

Register names are case insensitive: setq(A, foo) and setq(a, foo) both set the same register, and %qA and %qa both fetch its value.

::: seealso
- [R()]
- [LISTQ()]
- [LISTQ()]
- [LETQ()]
- [LOCALIZE()]
- [ulocal()]
- [REGISTERS()]
:::

## Register names and limits

Register names follow the same rules for attribute names, but they must be shorter than 64 characters in length.

Register names other than a-z or 0-9 have a per-localize limit, defined with @config max_named_qregs. If setq or setr tries to set a named q-register and it exceeds the limit, it will return the string "#-1 TOO MANY REGISTERS". This is the only time setq will return a string. setq() and setr() with registers a-z or 0-9 have nothing to worry about.

The maximum number of q-registers you can have set is configured via @config max_attrs_per_obj. That number is for the total number of q-registers set in a queue entry: Including across localize()d calls. Beyond that count, you can only use single character registers (a-z 0-9). Attempts to create a new register will simply fail silently, with the exception of setq().

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
> think setq(0,foo,one,bar)%q0%q`<one>` - [setq(0,r(one),one,%q0)]%q0%q`<one>`
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
