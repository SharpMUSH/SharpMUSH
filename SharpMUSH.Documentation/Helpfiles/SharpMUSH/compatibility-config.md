<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-config",
  "lookup": "compatibility config",
  "aliases": [],
  "sections": [
    {
      "id": "boolean-compatibility-tiny-booleans",
      "heading": "Boolean compatibility: `tiny_booleans`",
      "lookup": "compatibility config boolean compatibility tiny booleans"
    },
    {
      "id": "numeric-compatibility-tiny-math-null-eq-zero",
      "heading": "Numeric compatibility: `tiny_math`, `null_eq_zero`",
      "lookup": "compatibility config numeric compatibility tiny math null eq zero"
    },
    {
      "id": "trim-argument-order-tiny-trim-fun",
      "heading": "Trim argument order: `tiny_trim_fun`",
      "lookup": "compatibility config trim argument order tiny trim fun"
    },
    {
      "id": "parenthesis-groups-paren-groups",
      "heading": "Parenthesis groups: `paren_groups`",
      "lookup": "compatibility config parenthesis groups paren groups"
    },
    {
      "id": "logical-functions-mux-logic",
      "heading": "Logical functions: `mux_logic`",
      "lookup": "compatibility config logical functions mux logic"
    },
    {
      "id": "justification-mux-justify",
      "heading": "Justification: `mux_justify`",
      "lookup": "compatibility config justification mux justify"
    },
    {
      "id": "text-order-mux-collation",
      "heading": "Text order: `mux_collation`",
      "lookup": "compatibility config text order mux collation"
    },
    {
      "id": "tinymux-s-list-mux-list",
      "heading": "TinyMUX's `list()`: `mux_list`",
      "lookup": "compatibility config tinymux s list mux list"
    }
  ]
}
-->
# Compatibility Config

These configuration options change how existing code evaluates. All of them are read at evaluation
time, so changing one takes effect on the next call rather than at the next restart. Set them with
`@config/set` or on the web portal's configuration page.

## Boolean compatibility: `tiny_booleans`

Boolean conditions use the same rules in functions, command guards, list filters, and indirect calls.
With `tiny_booleans` off, numeric zero (including `-0`, `0.0` and hexadecimal zero), an empty string,
space-only text, and values beginning `#-` are false. Other text is true, including the word `false`.
A preserved tab is text, and a trailing space makes an otherwise numeric value text.

With `tiny_booleans` on, a leading signed integer determines truth. Thus `text` and `0.1` are false,
while `1.2text` is true. The compatibility conversion uses a signed 64-bit prefix, saturates
overflow, then takes its low 32 bits. For example, `4294967296` is false.

```sharp
> think t(-0.1)
1
> think t(-0)
0
```

`neq()` is the numeric inverse of `eq()`: it is true when not all arguments are equal. `condall()`
returns every result with a true condition; `ncond()` and `ncondall()` select false conditions. A
condition is a single value, not a list of separate booleans. A selected empty result does not cause
the default to run.

## Numeric compatibility: `tiny_math`, `null_eq_zero`

Arithmetic and numeric comparisons read `tiny_math` and `null_eq_zero` at call time, including
indirect `#apply` calls. With `tiny_math` off, the complete argument must be a number; an empty
argument is zero only when `null_eq_zero` is on. Scientific notation is accepted. Real-valued
arguments also accept hexadecimal notation, such as `0x10` and `-0x1.8p2` (16 and -6).

With `tiny_math` on, the leading numeric prefix is used, and text without one becomes zero:
`add(12foo,2)` returns 14 and `add(foo,2)` returns 2. Integer arithmetic takes an integer prefix, so
`div(1.5,1)` returns 1. Each function retains its existing signed, unsigned, decimal or
floating-point range; this setting does not remove range checks or give decimal arithmetic support
for infinity. Numeric input and output use a decimal point regardless of the server's language
settings.

`inc()` and `dec()` adjust a signed integer suffix. Without a suffix, they append 1 or -1 only when
`null_eq_zero` is on; otherwise they report an integer suffix error. Overflow reports
`#-1 OUT OF RANGE`. `tiny_math` does not change these string-counter rules.

## Trim argument order: `tiny_trim_fun`

With `tiny_trim_fun` off, `trim(text,characters,side)` uses Penn argument order. With it on,
`trim(text,side,characters)` uses Tiny argument order. The default characters are spaces and the
default side is both; `l` and `r` select the left or right side.

**Workaround.** Use `trimpenn(text,characters,side)` or `trimtiny(text,side,characters)` when a
package needs a fixed argument order regardless of game configuration.

```sharp
> think trimpenn(xxhixx,x,l)
hixx
```

## Parenthesis groups: `paren_groups`

**A choice.** Off by default; importing a PennMUSH database turns it on.

**PennMUSH** treats a `(` that starts no function call as opening a literal group: its commas are
text, and its `)` does not close the call around it, so `cat(x,(a,b)c)` has two arguments.<br>
**SharpMUSH**, with `paren_groups` off, treats that `(` as plain text. The first unescaped `)` closes
the call and the commas inside separate arguments, so the same call has three.<br>
**Why.** Whether a parenthesis groups then depends only on whether it follows a function name, and a
literal parenthesis is always written the same way. Imported worlds keep Penn's behaviour so migrated
softcode runs unchanged.<br>
**Workaround.** Escape literal parentheses inside function arguments with `\(` `\)` or `%(` `%)`.

```sharp
> think cat(x,(a,b)c)
x (a bc)
> think cat(x,%(a%,b%)c)
x (a,b)c
> think cat(x,\(a\,b\)c)
x (a,b)c
```

With the option on, the unescaped form groups as PennMUSH's does:

```sharp paren_groups
> think cat(x,(a,b)c)
x (a,b)c
```

## Logical functions: `mux_logic`

With `mux_logic` off, `and()`, `or()`, `xor()`, `cand()` and `cor()` test each argument the way
`t()` does, under `tiny_booleans`. With it on they test each argument by its leading signed integer,
as TinyMUX does, so `abc` is false and `2` is true, whatever `tiny_booleans` says. `xor()` is then
true for an odd number of true arguments, rather than for exactly one. `t()`, `not()`, `if()` and
the other functions keep the game's own rule, as they do in TinyMUX.

```sharp
> think [and(1,abc)]|[or(0,abc)]|[xor(1,1,1)]
1|1|0
```

With `mux_logic` on:

```sharp mux_logic
> think [and(1,abc)]|[or(0,abc)]|[xor(1,1,1)]
0|0|1
> think t(abc)
1
```

## Justification: `mux_justify`

With `mux_justify` off, `ljust()`, `rjust()` and `center()` leave text longer than the width as it
is, unless `ljust()` or `rjust()` is given a fourth argument of 1. With it on they cut it to the
width, as TinyMUX does, unless the fourth argument is 0.

```sharp
> think [ljust(abcdef,3)]|[center(abcdefg,3)]
abcdef|abcdefg
```

With `mux_justify` on:

```sharp mux_justify
> think [ljust(abcdef,3)]|[rjust(abcdef,3)]|[center(abcdefg,3)]
abc|abc|abc
> think ljust(abcdef,3,,0)
abcdef
```

## Text order: `mux_collation`

With `mux_collation` off, `sort()` with no sort type orders text ignoring case and keeps equal
items in the order they came, and `comp()` with no type compares character codes, so `B` comes
before `a`. With it on, both order text as TinyMUX does: by letter first, then lower case before
upper case. A sort type or a `comp()` type given in the call is unchanged.

```sharp
> think [sort(b A a B)]|[comp(a,B)]
A a b B|1
```

With `mux_collation` on:

```sharp mux_collation
> think [sort(b A a B)]|[comp(a,B)]|[sort(b A a B,a)]
a A b B|-1|A B a b
```

## TinyMUX's `list()`: `mux_list`

PennMUSH's `list()` names the game's functions, commands, flags and so on. TinyMUX's
`list(<list>, <action>[, <delimiter>])` evaluates `<action>` for each element, as `iter()` does,
and tells each result to the enactor instead of returning it. With `mux_list` on, `list()` is
TinyMUX's, and `list(functions)` is an argument-count error.

With `mux_list` on, this tells you `xa` and then `xb`, and `list()` itself returns nothing:

```sharp unchecked mux_list
> think list(a b,x%i0)
xa
xb
```
