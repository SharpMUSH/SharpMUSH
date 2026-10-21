<!-- help-article
{
  "corpus": "help",
  "id": "list-separators",
  "lookup": "list separators",
  "aliases": [
    "%*",
    "%*<",
    "separators",
    "marked separators"
  ],
  "sections": [
    {
      "id": "writing",
      "heading": "Writing a separator",
      "lookup": "list separators writing"
    },
    {"id": "keeping", "heading": "Where the mark is kept", "lookup": "list separators keeping"},
    {
      "id": "nesting",
      "heading": "Lists inside lists",
      "lookup": "list separators nesting"
    },
    {
      "id": "players",
      "heading": "Text a player typed",
      "lookup": "list separators players"
    },
    {
      "id": "seeing",
      "heading": "Seeing the separators",
      "lookup": "list separators seeing"
    }
  ]
}
-->
# List Separators

A list is text, and its delimiter is text, so an item that contains the delimiter breaks the list. A `|` in a box border, or one a player typed into a field, splits an item in two.

`%*<text>` marks `text` as a list separator. Give a list function a marked delimiter and it splits only where the same text is marked. An unmarked `|` stays inside its item.

```sharp
> think words(a|b%*<|>c, %*<|>)
2
> think words(a|b%*<|>c, |)
3
> think iter(a|b%*<|>c, [strlen(##)], %*<|>, -)
3-1
```

The mark is data, not display. The separator prints as its text, `stripansi()` keeps the mark, and comparisons ignore it, so `a|b%*<|>c` is equal to `a|b|c`.

Marked separators are opt-in. Every list function still defaults to a space, and a plain delimiter splits on every occurrence, marked or not, as it does in PennMUSH.

- [list separators writing]: what a separator can be
- [list separators keeping]: where a result keeps the mark
- [list separators nesting]: lists inside lists
- [list separators players]: text a player typed, and unmark()
- [list separators seeing]: showing which characters are marked

## Writing a separator

`%*<text>` evaluates `text` first, so a separator can be anything a function can return:

- `%*<|>` is a marked `|`.
- `%*<%r>` is a marked line break.
- `%*<::>` is a two-character separator.
- `%*<[v(SEP)]>` is whatever `SEP` holds, marked.

Inside the brackets, write a `>` as `\>`. Inside a function call, write a comma as `\,`. A space needs `%b`, since `%*< >` trims it away and marks nothing.

A marked delimiter is strict:

- It splits only on marked text. Given a list with no marks, it returns one item.
- It keeps empty items, even when the separator is a space. `%*<%b>` does not collapse runs the way a plain space does.
- Two marked stretches side by side are one, so `%*<|>%*<|>` is the same string as `%*<||>`. Split on `%*<|>`, each gives an empty item between the two.

## Where the mark is kept

An output separator that is marked keeps the result a list of its own:

```sharp
> think words(iter(x y, ##|##, %b, %*<|>), %*<|>)
2
```

The mark is kept when the text is stored by a function, such as `attrib_set()` or `set()`. An attribute set with `&` stores its text without evaluating it, so it holds the `%*<|>` code and makes the mark each time it is evaluated.

`@dolist/delimit` and `@map/delimit` take a marked delimiter too.

So do the layout functions that take a list: `lfields()`, `ldatatable()`, `lflex()`, `ltree()` and `lnode()`. A table's cells split on its `delim` option, which keeps the mark when the options are written out. `json()` builds plain text, so a delimiter passed through it loses the mark.

```sharp
> think ldatatable({{"border":"ascii","delim":"%*<^>"}},Name%*<^>Motto,Raya%*<^>a^b%*<|>Mannaz%*<^>c,%*<|>)
Name    Motto
-------------
Raya    a^b
Mannaz  c
```

## Lists inside lists

A separator carries no name, so lists inside lists use different text for each level:

```sharp
> think iter(iter(Mannaz:12 Raya:9, edit(##, :, %*<^>), %b, %*<|>), [first(##, %*<^>)] has [rest(##, %*<^>)], %*<|>, %r)
Mannaz has 12
Raya has 9
```

Pick separators that don't contain each other, such as `|` and `^`, not `|` and `||`.

## Text a player typed

A player can write `%*<|>` in a command's arguments, the same way they can write colour, and it reaches a `$`-command's `%0` marked. Take the marks off text a player typed before it goes into a list, with `unmark()`. It keeps the colour. `secure()` and `escape()` take the marks off too.

```sharp
> &CMD`ADD me=$+add *:think attrib_set(me/LIST, [v(LIST)]%*<|>[unmark(%0)])
> +add x
> +add a%*<|>b
> think words(rest(v(LIST), %*<|>), %*<|>)
2
```

## Seeing the separators

Printed, a separator looks like any other character. Two ways show which characters are marked:

- `decompose()` and `@decompile` write each marked stretch back as `%*<...>`. Evaluating the result gives the same list.
- A player set `SHOW_SEPARATORS` sees separators in reverse video, in a terminal and in the portal.

```sharp
> think decompose(Mannaz%*<^>12%*<|>Raya%*<^>9)
Mannaz%*<^>12%*<|>Raya%*<^>9
```

PennMUSH, RhostMUSH and TinyMUX have no `%*` substitution and print `*<|>` for `%*<|>`. Importing a PennMUSH database names every attribute that uses `%*<`.

::: seealso
- [unmark()]
- [SHOW_SEPARATORS]
- [decompose()]
- [@dolist]
- [iter()]
- [substitutions]
- [compatibility parser]
:::
