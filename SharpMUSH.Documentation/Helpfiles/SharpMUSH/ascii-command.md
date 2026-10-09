<!-- help-article
{
  "corpus": "help",
  "id": "ascii-command",
  "lookup": "@ascii",
  "aliases": ["ascii translations", "ascii_translations"],
  "sections": []
}
-->
# @ascii

`@ascii`<br>
`@ascii <character>=<text>`<br>
`@ascii/remove <character>`

A client that is not sent UTF-8 gets each character it cannot show as the nearest one it can (see [CHARSET]). `@ascii` keeps the game's own table of stand-ins, which come before the built-in ones.

`@ascii` with no argument lists the table: each character, its code point, and the text sent for it.

`@ascii <character>=<text>` sets the text sent for `<character>`, which is one character outside ASCII. The table holds each character once, so setting it again replaces its text. `<text>` is plain ASCII and is evaluated, so write a space as `%b`; it may be longer than the character, and an empty `<text>` leaves the character out. A box or a table is laid out after the stand-ins are applied, so it stays lined up; text you padded yourself may not.

`@ascii/remove <character>` takes a character out of the table, so it gets the built-in stand-in again.

The table is kept with the game's configuration and is the same one the web portal's ASCII Translations page edits. Using `@ascii` needs the config.admin permission.

Example:
```
> @ascii <a middle dot>=%b-%b
```
Output: the game says a middle dot is now sent to clients without Unicode as a space, a dash and a space. A title written as Wren, a middle dot, then Scene 3 reaches an ASCII client as `Wren - Scene 3` rather than `Wren * Scene 3`.

::: seealso
- [CHARSET]
- [@SOCKSET]
- [LAYOUT BORDERS ASCII]
:::
