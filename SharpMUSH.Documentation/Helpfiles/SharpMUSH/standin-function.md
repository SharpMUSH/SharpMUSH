<!-- help-article
{
  "corpus": "help",
  "id": "standin-function",
  "lookup": "standin()",
  "aliases": [],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "standin() examples"}
  ]
}
-->
# standin()

`standin(<text>, <stand-in>)`

Returns `<text>`, sent as `<stand-in>` to a client that cannot show it. A client that is not sent UTF-8 gets each character it cannot show as the nearest one it can (see [CHARSET]), and a picture character such as an emoji becomes `??`. `standin()` says what to send instead, for this text only.

`<stand-in>` replaces the whole of `<text>` when any character in it would be replaced, and comes before the game's [@ASCII] table. A client that shows all of `<text>` gets `<text>`, so a Latin-1 client keeps an accented letter. `<stand-in>` is plain ASCII, or empty to leave `<text>` out; anything else returns `#-1 INVALID ARGUMENT`. A table or a box lays the stand-in out at its own width, so columns stay lined up.

- [standin() examples]: a pinned mark, and a check mark

::: seealso
- [CHARSET]
- [@ASCII]
- [STRIPACCENTS()]
:::

## Examples

```sharp
> think standin(chr(128204),P) Rules for new players
```
Output: a UTF-8 client sees a pin emoji before `Rules for new players`, and an ASCII client sees `P Rules for new players`.

```sharp
> think standin([chr(10003)] done,\[x\] done)
```
Output: a check mark and `done` for a UTF-8 client, and `[x] done` for an ASCII one, where the built-in stand-ins would send `+ done`.
