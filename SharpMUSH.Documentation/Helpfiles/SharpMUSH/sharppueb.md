# HTML

Hyper Text Markup Language (http://www.w3.org)

Three kinds of client receive HTML tags: Pueblo clients (see [pueblo]), MXP clients, and the web portal. To utilize HTML, use one of the MUSH HTML Functions (see [HTML FUNCTIONS] for a list).

HTML tags are stripped when sent to any other client.

Pueblo and MXP are different dialects, not one extending the other. Most formatting tags are spelled alike, but a command link is not: Pueblo writes `<a xch_cmd="...">` and MXP writes `<send href="...">`, and each client prints the other's as text. Use [CMDLINK()] for a command link, and each client gets its own.


::: seealso
- [HTML FUNCTIONS]
- [pueblo]
- [HTML()]
:::

# pueblo()

`pueblo(<player|descriptor>)`

This function returns 1 if the given player or descriptor is currently Pueblo-enabled, and 0 otherwise. An MXP client is not a Pueblo client: it returns 0 unless the client also sent the Pueblo handshake. [TERMINFO()] lists "mxp" for an MXP client.

If used on a player/descriptor which is not connected, pueblo() returns #-1 NOT CONNECTED. Mortals can only give a *<descriptor>* for their own connections (but can give any *<player>* arg), while See_All objects can check any descriptor.

When used with a *<player>* argument, the most recently active connection is used if the *<player>* is logged in more than once.


::: seealso
- [TERMINFO()]
- [HTML()]
- [pueblo]
:::

::: seealso
- [HTML]
- [pueblo]
:::

# HTML Functions

HTML Functions are used to output HTML tags to HTML capable users: Pueblo and MXP clients, and the web portal. These tags will be stripped by the system for anything non-HTML related.

|                    |                    |                    |                    |
|--------------------|--------------------|--------------------|--------------------|
| [CMDLINK()]        | [ENDTAG()]         | [HTML()]           | [TAG()]            |
| [TAGWRAP()]        | [WEBSOCKET_HTML()] | [WSHTML()]         |                    |

Things a client may have that are not tags (a sound, a picture, a pane, a screen to clear) are written once and rendered by each client in its own way. See [MEDIA FUNCTIONS].

### Examples
```sharp
> say tagwrap(a,href="https://sharpmush.com",SharpMUSH)
> say cmdlink(Who is online?,+who)
> say wshtml(<a href="https://sharpmush.com">SharpMUSH</a>)
```

Mortals are restricted in the tags they may use. Most standard HTML tags are ok; command links (cmdlink(), and the SEND and XCH_CMD parameters) can only be sent by Wizards or those with the Send_OOB @power.

# html()

`html(<string>)`

In PennMUSH this wizard-only function outputs *<string>* as a single HTML tag. SharpMUSH's markup is a span over the text it applies to, so it has no single tag to write, and html() returns `#-1 USE TAGWRAP INSTEAD`. Use [TAGWRAP()].


::: seealso
- [pueblo]
- [HTML]
- [HTML FUNCTIONS]
:::

# tag()

`tag(<name>[, <param1>[, ... , <paramN>]])`

In PennMUSH this outputs an opening HTML/Pueblo tag. SharpMUSH's markup is a span over the text it applies to, so it has no tag without an end, and tag() returns `#-1 USE TAGWRAP INSTEAD`. Use [TAGWRAP()].


::: seealso
- [ENDTAG()]
- [TAGWRAP()]
- [HTML()]
:::

# endtag()

`endtag(<name>)`

In PennMUSH this outputs a closing HTML/Pueblo tag. As with [TAG()], SharpMUSH returns `#-1 USE TAGWRAP INSTEAD`. Use [TAGWRAP()].


::: seealso
- [TAG()]
- [TAGWRAP()]
- [HTML()]
:::

# cmdlink()

`cmdlink(<string>, <command>[, <hint>])`

Outputs *<string>* as a link that runs *<command>* when clicked. *<hint>*, which defaults to *<command>*, is shown as a tooltip.

Each client gets the link in its own dialect: `<a xch_cmd>` for Pueblo, `<send href>` for MXP, a clickable link in the web portal. Every other client gets *<string>* alone. This is why a command link is a function of its own: one written with tagwrap() works in only one of those clients.

cmdlink() is SharpMUSH's own. It needs a Wizard or the Send_OOB @power, as PennMUSH requires for XCH_CMD; anyone else gets `#-1 PERMISSION DENIED`. A *<command>* containing a control character (a line break, a tab, an escape) is `#-1 INVALID ARGUMENT`: a line break would have the client send the rest as a second command, and the others are not part of any command a player could type.

Clicking a link sends *<command>* as if the reader had typed it, so it runs as whoever clicks, not as the object that made the link, and anyone can type the same line. A command behind a link checks who is running it (`%#`) the way any command does. [Chat Cards](https://sharpmush.com/guides/chat-cards/) on the SharpMUSH documentation site works through an example.

### Example
```sharp
> think cmdlink(Who is online?,+who,List the connected players)
```

::: seealso
- [TAGWRAP()]
- [HTML FUNCTIONS]
:::

# Media Functions

Sounds, pictures, panes and the rest are said once, and each client is told about them in the way that client has. An MXP client gets MXP's elements, a Pueblo client its `xch_` ones, the web portal an HTML element it can act on, and a plain telnet client either nothing or the words that stand in for what it cannot show.

|                 |                 |                 |                 |
|-----------------|-----------------|-----------------|-----------------|
| [CLEARSCREEN()] | [EXPIRELINKS()] | [FIGURE()]      | [IMAGE()]       |
| [MUSIC()]       | [PANE()]        | [PREFETCH()]    | [PREFORMAT()]   |
| [SOUND()]       | [STOPSOUND()]   |                 |                 |

figure() is a picture with text art and text flowing round it; it is described with the [LAYOUT FUNCTIONS].

None of them leaves anything in the plain text, so `strlen()` and listen patterns see what they saw before, except where a function stands words in for what a client cannot show, such as a picture's description.

All but preformat() need a Wizard or the Send_OOB @power, as [CMDLINK()] does: they make a client fetch a file, play it, or clear what the player is looking at. Laying text out does not, so preformat() is open to anyone. image() and figure() also accept the Send_Image @power, which allows pictures and nothing else; the approved role holds it, so an approved character can show a picture in a pose or a description.

::: seealso
- [HTML FUNCTIONS]
:::

# sound()

`sound(<file>[, <volume>[, <repeats>]])`

Plays a sound effect. *<file>* is a file the client resolves against the game's own sound directory, or an absolute address. *<volume>* is 0 to 100. *<repeats>* is how many times to play it, or `-1` to play it until it is stopped.

MXP gets `<SOUND>`, Pueblo `<img xch_sound>`, the web portal an `<audio>` element the page decides whether to play, and a plain telnet client nothing at all.

A client such as Mudlet that lists Client.Media in GMCP Core.Supports is sent GMCP Client.Media.Play before the line, with *<repeats>* as its loops. A full address is fetched from where it says; a file name is fetched from the game's `media_base_url` (see [@config net]).

### Example
```sharp
> @pemit %#=[sound(door.wav,80)]The door creaks open.
```

::: seealso
- [MUSIC()]
- [STOPSOUND()]
- [MEDIA FUNCTIONS]
:::

# music()

`music(<file>[, <volume>[, <repeats>]])`

As [SOUND()], for background music: one piece plays at a time, and MXP has a channel of its own for it. `music(theme.mid,,-1)` plays until something stops it.

Mudlet is sent Client.Media.Play as music under the key `music`, so a new piece replaces the one playing.

::: seealso
- [SOUND()]
- [STOPSOUND()]
:::

# stopsound()

`stopsound([<channel>])`

Silences what is playing. *<channel>* is `effects` or `music`; with none, both stop.

Mudlet is sent Client.Media.Stop for sounds, for music, or with nothing named, for everything.

::: seealso
- [SOUND()]
- [MUSIC()]
:::

# image()

`image(<address>[, <description>[, <width>[, <height>]]])`

A picture. *<width>* and *<height>* are in pixels.

*<description>* is what a client with no pictures shows instead (the address itself when none is given), so it is worth writing. MXP gets `<IMAGE>`, Pueblo and the portal `<img>`, and a terminal the words.

Put it inside [CMDLINK()] for a picture that runs a command when clicked.

It needs a Wizard, the Send_OOB @power or the Send_Image @power (which the approved role holds); anyone else gets `#-1 PERMISSION DENIED`. A host the `image_hosts` option refuses is shown as the words.

### Example
```sharp
> @pemit %#=image(map.png,A map of the city,200)
> @pemit %#=cmdlink(image(map.png,A map),look map)
```

# pane()

`pane(<text>, <name>[, <title>])`

Sends *<text>* to a pane of its own (a window or region the client keeps apart from the main output), opening it if the client has none by that name. *<title>* is what the pane is labelled.

A client with no panes shows the text where it is, which is why the text is inside the function rather than sent after it.

### Example
```sharp
> @pemit %#=pane(u(fun`map),map,The Map)
```

# preformat()

`preformat(<text>)`

Says *<text>* is laid out by its own spacing: a table, a map, a listing.

A client reading the stream as HTML (a Pueblo client, the portal) collapses runs of spaces and uses a variable width font, so anything drawn with spaces needs this around it or its columns will not line up. Pueblo gets `<xch_mudtext>`, the portal `<pre>`, and a terminal the text unchanged, since a terminal lays it out that way already.

[align()], [align()] and [TABLE()] already say it for themselves, as do tables and code blocks in help and wiki text. This is for columns you draw yourself.

### Example
```sharp
> &cmd`who Globals=$+who: @nspemit %#=preformat(u(fun`who))
```

::: seealso
- [align()]
- [TABLE()]
- [pueblo client enhancements]
:::

# clearscreen()

`clearscreen()`

Clears what the player has been shown. A terminal is sent the ANSI sequence for it, Pueblo `<xch_page clear="text">`, and the portal an element it acts on. MXP has no such instruction, and an MXP client is sent nothing.

# prefetch()

`prefetch(<address>)`

Asks the client to fetch something now that it will want soon, so it is already there when it is used. Pueblo and the portal act on it; every other client is sent nothing.

# expirelinks()

`expirelinks([<group>])`

Makes links already on the player's screen stop working: those in *<group>*, or every one when no group is named. MXP acts on it, the portal is told, and a client with neither leaves its old links working.

::: seealso
- [CMDLINK()]
:::
