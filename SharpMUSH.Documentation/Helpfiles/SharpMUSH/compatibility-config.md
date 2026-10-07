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
