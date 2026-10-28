<!-- help-article
{
  "corpus": "help",
  "id": "mogrifier",
  "lookup": "mogrifier",
  "aliases": ["mogrifying", "MOGRIFY"],
  "sections": [
    {"id": "deciding", "heading": "Deciding what happens", "lookup": "mogrifier deciding"},
    {"id": "parts", "heading": "Rewriting the parts", "lookup": "mogrifier parts"},
    {"id": "format", "heading": "Formatting the whole line", "lookup": "mogrifier format"},
    {"id": "members", "heading": "Members joining and leaving", "lookup": "mogrifier members"}
  ]
}
-->
# mogrifier

A channel's mogrifier is an object whose MOGRIFY\`* attributes rewrite each line said on the channel. `@channel/mogrifier <channel>=<object>` sets it. A speaker who fails the object's `@lock/use` is not mogrified. Every attribute is optional, and one that is absent or evaluates to nothing leaves its part of the line as it was. They run on the mogrifier, with the speaker as %#.

Each attribute gets its values by position and by the name shown (see [named arguments]), so `%<message>` reads the message whatever position it has in that attribute.

- [mogrifier deciding]: blocking a line, and skipping formats and the buffer
- [mogrifier parts]: rewriting each part of the line
- [mogrifier format]: replacing the whole line, with an example
- [mogrifier members]: messages for a member who joins, leaves, gags or ungags

## Deciding what happens

These three decide what happens to the line. Each gets %0 or `%<type>` (the line's type, as in [@chatformat]), %1 or `%<channel>` (the channel's name), %2 or `%<message>`, %3 or `%<name>` (the speaker's name) and %4 or `%<title>`:
- MOGRIFY\`BLOCK: a result that is not empty refuses the line, and the speaker is shown the result.
- MOGRIFY\`OVERRIDE: a true result stops the members' own `@chatformat` from running.
- MOGRIFY\`NOBUFFER: a true result keeps the line out of the channel's recall buffer.

## Rewriting the parts

These rewrite one part of the line each, in this order: MOGRIFY\`CHANNAME (the bracketed `<Name>`), MOGRIFY\`TITLE, MOGRIFY\`PLAYERNAME, MOGRIFY\`SPEECHTEXT (the "says", for a said line only) and MOGRIFY\`MESSAGE. Each gets %0 or `%<value>` (its part as it stands), %1 or `%<channel>`, %2 or `%<type>`, %3 or `%<message>` (as the speaker sent it), %4 or `%<title>`, %5 or `%<name>`, %6 or `%<says>` and %7 or `%<options>` (as in [@chatformat]). The title, name and says hold what the parts before have made of them.

## Formatting the whole line

MOGRIFY\`FORMAT, when it returns something, is the whole line every member is sent. It gets the values [@chatformat] gets, by the same names: %0 or `%<type>`, %1 or `%<channel>` (the bracketed name, as MOGRIFY\`CHANNAME left it), %2 or `%<message>`, %3 or `%<name>`, %4 or `%<title>`, %5 or `%<default>` (the line as it would be sent), %6 or `%<says>` and %7 or `%<options>`.

```sharp
&MOGRIFY`MESSAGE #123=[ucstr(%<value>)]
&MOGRIFY`FORMAT #123=%<channel> %<name>: %<message>
```
Output: when Walker says hello on Public, whose mogrifier is #123, every member sees `<Public> Walker: HELLO`.

## Members joining and leaving

Four attributes on the mogrifier speak to one member, the way TinyMUX's channel object does. Each runs on the mogrifier with the member as %#, and the member is told what it returns:
- COMJOIN: when the member joins, by `@channel/on` or `addcom`.
- COMLEAVE: when the member leaves, by `@channel/off`, `@cboot` or the last `delcom` for the channel.
- COMOFF: when the member gags the channel, by `@channel/gag` or `allcom off`.
- COMON: when the member ungags it, by `@channel/ungag` or `allcom on`.

A TinyMUX import makes each channel object the channel's mogrifier, so these come with it.

```sharp
&COMJOIN #123=Welcome to the channel, %n. Be kind.
```
Output: when Walker joins Public, whose mogrifier is #123, Walker sees `Welcome to the channel, Walker. Be kind.`

::: seealso
- [@CHANNEL ADMIN]
- [@chatformat]
- [named arguments]
:::
