<!-- help-article
{
  "corpus": "help",
  "id": "wshtml-function",
  "lookup": "wshtml()",
  "aliases": [],
  "sections": [
    {"id": "permissions", "heading": "Permissions", "lookup": "wshtml() permissions"},
    {"id": "compatibility", "heading": "PennMUSH compatibility", "lookup": "wshtml() compatibility"},
    {"id": "example", "heading": "Example", "lookup": "wshtml() example"}
  ]
}
-->
# wshtml()

`wshtml(<html>[, <default>])`

  Turns an HTML fragment into markup and returns it, for whatever emits it to deliver: text becomes the text, each element becomes a tag over what it encloses, exactly as [TAGWRAP()] builds one tag at a time. A WebSocket, Pueblo or MXP client receives the tags; an ANSI client gets the styling it can show for `<b>`, `<i>`, `<u>` and `<s>` and the words for everything else; everything else gets the words. Nothing is sent by the function itself, and the value stores, slices and re-evaluates like any other string.

  The fragment is read the way a browser reads it: an unclosed tag closes at the end, a stray closing tag is dropped, a bare `<` is text. An element that encloses nothing is dropped, since a tag has to cover something; `<br>` is a line break.

- [wshtml() permissions]: which tags and attributes are allowed
- [wshtml() compatibility]: `<default>` and PennMUSH's `wsjson()`
- [wshtml() example]: a link, as each client sees it

## Permissions

  The gate is [TAGWRAP()]'s. With the Send_OOB power (Pueblo_Send is its older name), any tag and any attribute. Without it, only the tags PennMUSH's `tagwrap()` allows (one forbidden tag refuses the whole fragment) and only attributes a browser cannot be made to run, one forbidden attribute dropping them all from that tag.

## PennMUSH compatibility

  PennMUSH's `wshtml(<html>, <default>)` takes a second, plain-text string for clients without HTML, and its `wsjson()` embeds a JSON object the same way. Here `<default>` is accepted, so softcode written for PennMUSH still runs, but it is not used: the markup's own text is what a client without HTML sees. There is no `wsjson()`: JSON is data for a program rather than text with a plain reading, so it goes by [OOB()], which sends it where a connection can receive it.

## Example

  For example:

```sharp
@emit [wshtml(<a href="https://sharpmush.com">SharpMUSH</a>)]
```

  A player on the web portal or a Pueblo/MXP client sees a link reading SharpMUSH; a telnet player, and any listening object, sees `SharpMUSH`.

::: seealso
- [WEBSOCKETS]
- [pueblo]
- [HTML FUNCTIONS]
- [JSON FUNCTIONS]
:::
