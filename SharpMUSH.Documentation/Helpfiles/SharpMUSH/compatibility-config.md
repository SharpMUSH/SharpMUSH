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
      "id": "rhostmush-edit-modes-rhost-edit",
      "heading": "RhostMUSH edit() modes: `rhost_edit`",
      "lookup": "compatibility config rhostmush edit modes rhost edit"
    },
    {
      "id": "rhostmush-lattr-arguments-rhost-lattr",
      "heading": "RhostMUSH lattr() arguments: `rhost_lattr`",
      "lookup": "compatibility config rhostmush lattr arguments rhost lattr"
    },
    {
      "id": "rhostmush-map-arguments-rhost-map-args",
      "heading": "RhostMUSH map() arguments: `rhost_map_args`",
      "lookup": "compatibility config rhostmush map arguments rhost map args"
    },
    {
      "id": "rhostmush-printf-fields-rhost-printf",
      "heading": "RhostMUSH printf() fields: `rhost_printf`",
      "lookup": "compatibility config rhostmush printf fields rhost printf"
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

## RhostMUSH edit() modes: `rhost_edit`

Off by default; meant for a game moved from RhostMUSH.

With `rhost_edit` off, edit() reads its arguments after the string as from/to pairs, as PennMUSH does,
so `edit(abcabc,b,X,c,Y)` makes two edits. With it on, a fourth and fifth argument are RhostMUSH's
mode arguments instead. A fourth that starts with a nonzero number replaces only the first match. The
fifth chose how RhostMUSH edited ANSI codes; markup in SharpMUSH is never part of the text, so it is
accepted and changes nothing. Three arguments mean the same either way, and more than five are an
error with the option on.

```sharp rhost_edit
> think edit(abcabc,b,X,1)
aXcabc
> think edit(abcabc,b,X,c,Y)
aXcaXc
```

## RhostMUSH lattr() arguments: `rhost_lattr`

Off by default; meant for a game moved from RhostMUSH.

With `rhost_lattr` off, lattr() and lattrp() take an object and pattern and an optional output
delimiter, as PennMUSH does. With it on, they take RhostMUSH's arguments:

`lattr(<object>[/<pattern>][, <target>[, <type>[, <flags>[, <regexp>[, <tree>]]]]])`

- `<target>` is a player whose view to list: only the attributes they may see are named. It is used
  only when you control that player; otherwise the list is your own view. The names are always
  separated by a space.
- `<type>` narrows or summarises the list by its first character: `$` keeps attributes holding a
  $-command and `^` those holding a ^-listen pattern; `&` keeps attributes with every attribute flag
  whose letter is in `<flags>`, and `|` those with any of them; `l` answers the number of names and
  of pages; a number answers that page (243 names a page, as on a default RhostMUSH); `~<start>-<count>`
  answers `<count>` names from the `<start>`th; `>` and `<` answer the alphabetically last or first
  name. `+` lists RhostMUSH's lock attributes, which SharpMUSH does not have, so it answers nothing; see
  llocks().
- `<regexp>`, when nonzero, makes `<pattern>` a regular expression.
- `<tree>`, when nonzero, makes `*` stop at a backtick, as SharpMUSH and PennMUSH match attribute
  trees. RhostMUSH has no attribute trees, so without it `*` also matches a backtick and
  `lattr(<object>)` names every attribute, branches' leaves included.

```sharp rhost_lattr
> &RHOSTDOC_LATTR_CMD me=$rhostdoc:think hi
> &RHOSTDOC_LATTR_TEXT me=hello
> think lattr(me/RHOSTDOC_LATTR_*,,$)
RHOSTDOC_LATTR_CMD
> think lattr(me/RHOSTDOC_LATTR_*,,l)
2 1
> think lattr(me/RHOSTDOC_LATTR_*,|)
RHOSTDOC_LATTR_CMD RHOSTDOC_LATTR_TEXT
```

## RhostMUSH map() arguments: `rhost_map_args`

Off by default; meant for a game moved from RhostMUSH.

map() passes any arguments after its output separator to the attribute as %1, %2 and so on. With
none, `rhost_map_args` off passes the item's position as %1, as PennMUSH does; with it on, %1 is empty,
as on RhostMUSH.

```sharp rhost_map_args
> &RHOSTDOC_MAP me=<%0|%1>
> think map(me/RHOSTDOC_MAP,a b)
<a|> <b|>
> think map(me/RHOSTDOC_MAP,a b,,,x)
<a|x> <b|x>
```

## RhostMUSH printf() fields: `rhost_printf`

Off by default; meant for a game moved from RhostMUSH.

With `rhost_printf` off, printf() reads `%` directives in SharpMUSH's own grammar (see printf()).
With it on, it reads RhostMUSH's fields instead: a field is `$`, then any of the codes below, then
`s`, and it takes the next value; `$$` is a dollar sign, and everything else is copied as it is.
printf() then needs at least one value. A value with no field left for it is dropped, and a field
with no value left is empty.

- A number is the field's width; a value longer than it is cut, and a shorter one padded. Without
  one the field is as wide as its value. A width starting with `0` pads with zeros.
- `-` aligns left, `^` centres and `_` spreads the words across the width; the default is right.
  Giving two of these is an error.
- `+` never cuts the value, and `*` cuts from its start rather than its end.
- `|` wraps the value onto further rows at the width, and `|"` wraps at a space where it can. `&`
  starts a new row at each line of the value. Further rows show the text between fields as spaces.
- `:<character>:` pads with that character rather than a space.
- `.` leaves out a blank last row. `!` leaves out an empty field and the text after it.

The less common codes (`@ < > ' `` ` `` # / ;`) are read as on RhostMUSH as well.

```sharp rhost_printf
> think printf(|$-5s|$5s|,ab,cd)
|ab   |   cd|
> think printf(<$10:.:s>,ab)
<........ab>
```
