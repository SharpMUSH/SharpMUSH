# PUEBLO

Pueblo is a client made by Chaco (a now defunct company). It attempts to mix HTML with MUSH. There are other clients (notably MUSHclient) that also offer Pueblo features. SharpMUSH can offer support for some of the enhanced features of Pueblo, enabled via the 'pueblo' @config option.

SharpMUSH will automatically detect a Pueblo client (rather, the client will announce itself and SharpMUSH will detect that), and set up that connection for Pueblo use. 


**See Also:**
- [pueblo2]

# PUEBLO2

SharpMUSH makes the following enhancements visible to Pueblo users when Pueblo support is enabled:

* Object/Room names are highlighted
* Unordered list for contents and transparent exits
* Contents and exits lists have links (Click an exit to walk through it)
* Object lists (like the ones found in 'examine'/'inventory') have links
* Conversion of accented characters into &entity; codes

While Pueblo brings a number of new features and markups to MUSHes, in many ways it's not well suited. Because it's based on HTML, multiple spaces are compressed, and Pueblo typically defaults to a variable width font. Because of this, supporting Pueblo is not just a matter of enabling the option. The output of any commands which rely on fixed spacing, such as a +who, must be wrapped in `<pre>` tags to ensure they appear correctly for players using Pueblo. For instance:

```sharp
> &cmd`who Globals=$+who: @nspemit %#=tagwrap(pre, u(fun`who))
```


**See Also:**
- [pueblo()]
- [HTML Functions]

# HTML

Hyper Text Markup Language (http://www.w3.org)

Three kinds of client receive HTML tags: Pueblo clients (see [pueblo]), MXP clients, and the web portal. To utilize HTML, use one of the MUSH HTML Functions (see [HTML Functions] for a list).

HTML tags are stripped when sent to any other client.

Pueblo and MXP are different dialects, not one extending the other. Most formatting tags are spelled alike, but a command link is not: Pueblo writes `<a xch_cmd="...">` and MXP writes `<send href="...">`, and each client prints the other's as text. Use [cmdlink()] for a command link, and each client gets its own.


**See Also:**
- [HTML Functions]
- [PUEBLO]
- [html()]

# PUEBLO()

`pueblo(<player|descriptor>)`

This function returns 1 if the given player or descriptor is currently Pueblo-enabled, and 0 otherwise. An MXP client is not a Pueblo client: it returns 0 unless the client also sent the Pueblo handshake. [terminfo()] lists "mxp" for an MXP client.

If used on a player/descriptor which is not connected, pueblo() returns #-1 NOT CONNECTED. Mortals can only give a *<descriptor>* for their own connections (but can give any *<player>* arg), while See_All objects can check any descriptor.

When used with a *<player>* argument, the most recently active connection is used if the *<player>* is logged in more than once.


**See Also:**
- [terminfo()]
- [html()]
- [PUEBLO]

**See Also:**
- [HTML]
- [PUEBLO]

# HTML FUNCTIONS

HTML Functions are used to output HTML tags to HTML capable users: Pueblo and MXP clients, and the web portal. These tags will be stripped by the system for anything non-HTML related.

Available functions:
- tagwrap()
- cmdlink()
- wshtml()
- html(), tag() and endtag() (see their entries)

### Examples
```sharp
> say tagwrap(a,href="https://sharpmush.com",SharpMUSH)
> say cmdlink(Who is online?,+who)
> say wshtml(<a href="https://sharpmush.com">SharpMUSH</a>)
```

Mortals are restricted in the tags they may use. Most standard HTML tags are ok; command links (cmdlink(), and the SEND and XCH_CMD parameters) can only be sent by Wizards or those with the Send_OOB @power.

# HTML()

`html(<string>)`

In PennMUSH this wizard-only function outputs *<string>* as a single HTML tag. SharpMUSH's markup is a span over the text it applies to, so it has no single tag to write, and html() returns `#-1 USE TAGWRAP INSTEAD`. Use [tagwrap()].


**See Also:**
- [PUEBLO]
- [HTML]
- [HTML Functions]

# TAG()

`tag(<name>[, <param1>[, ... , <paramN>]])`

In PennMUSH this outputs an opening HTML/Pueblo tag. SharpMUSH's markup is a span over the text it applies to, so it has no tag without an end, and tag() returns `#-1 USE TAGWRAP INSTEAD`. Use [tagwrap()].


**See Also:**
- [endtag()]
- [tagwrap()]
- [html()]

# ENDTAG()

`endtag(<name>)`

In PennMUSH this outputs a closing HTML/Pueblo tag. As with [tag()], SharpMUSH returns `#-1 USE TAGWRAP INSTEAD`. Use [tagwrap()].


**See Also:**
- [tag()]
- [tagwrap()]
- [html()]

# TAGWRAP()

`tagwrap(<name>[, <parameters>], <string>)`

This function outputs *<string>*, wrapped in the *<name>* HTML tag with the specified *<parameters>*.

The tag is markup on *<string>*, not text in it. A Pueblo client, an MXP client and the web portal receive it as a tag; every other client receives *<string>* alone. strlen() and the other string functions see only *<string>*.

### Example
```sharp
tagwrap(a,href="https://sharpmush.com",SharpMUSH Downloads)
```

Will output (in HTML):
```html
<a href="https://sharpmush.com">SharpMUSH Downloads</a>
```

The tag is written as given, so it has to mean something in the reader's client. Most tags are spelled the same in Pueblo and MXP, but command links are not: use [cmdlink()] for one.

Without Send_OOB, *<name>* must be one of PennMUSH's allowed tags (A, B, I, U, FONT, PRE, IMG, TABLE and the like; anything else is `#-1 PERMISSION DENIED`), and the *<parameters>* are kept only if every one is on SharpMUSH's list of presentational attributes (href, src, color, face, size, align, width, height, title, xch_hint, class, and a few more), with any href or src naming an http, https, mailto, ftp or tel address. If any parameter fails, all of them are dropped. This is stricter than PennMUSH because the web portal renders the tag in a browser. A *<name>* that is not letters and digits is `#-1 INVALID TAG NAME` for everyone.

A particularly important use of this function is `tagwrap(pre, <string>)`. Because Pueblo works like an html browser, spaces and tabs are compressed to a single space. If you have code (a +who function, for example) that relies on exact spacing, surround its output with a tagwrap(pre,...) so that Pueblo will render it as "preformatted" text.


**See Also:**
- [cmdlink()]
- [tag()]
- [endtag()]
- [html()]

# CMDLINK()

`cmdlink(<string>, <command>[, <hint>])`

Outputs *<string>* as a link that runs *<command>* when clicked. *<hint>*, which defaults to *<command>*, is shown as a tooltip.

Each client gets the link in its own dialect: `<a xch_cmd>` for Pueblo, `<send href>` for MXP, a clickable link in the web portal. Every other client gets *<string>* alone. This is why a command link is a function of its own: one written with tagwrap() works in only one of those clients.

cmdlink() is SharpMUSH's own. It needs a Wizard or the Send_OOB @power, as PennMUSH requires for XCH_CMD; anyone else gets `#-1 PERMISSION DENIED`. A *<command>* containing a control character — a line break, a tab, an escape — is `#-1 INVALID ARGUMENT`: a line break would have the client send the rest as a second command, and the others are not part of any command a player could type.

### Example
```sharp
> think cmdlink(Who is online?,+who,List the connected players)
```

**See Also:**
- [tagwrap()]
- [HTML Functions]

# WEBSOCKETS

WebSockets are a network protocol used by JavaScript-enabled web browsers to make persistent network connections, similar to the telnet connection you use to connect to SharpMUSH. With WebSockets enabled in mush.cnf, it is possible to connect from MUSH clients embedded in HTML pages using JavaScript. A WebSocket client can natively render HTML, but can also parse Pueblo links into HTML links that send a command to the MUSH when clicked. For safety, we separate plain text from the other kinds of HTML/Pueblo code that we want rendered. In order to render HTML/Pueblo, a player with the Pueblo_Send power uses special functions to embed HTML/Pueblo markup. Players without the PUEBLO_SEND power can not use these markup functions. Any HTML code strings that are not properly marked up will simply show up as unrendered plain text.

See https://github.com/grapenut/websockclient for an example client.

The different kinds of markup that can be sent to clients are:
* Plain text
* HTML tags
* Pueblo links
* JSON objects
* Prompts

Without using any HTML markup functions, output is rendered as normal plain text (including ANSI and xterm256 color).

See [HTML Functions] for functions used to embed HTML markup tags one at a time.<br>
See [wshtml()] for help embedding large segments of raw HTML markup to be sent to WebSocket clients.

For clickable command links, use [cmdlink()], e.g. `cmdlink(Who is online?,+who)`. The web portal and the example client above both follow it, and so do Pueblo and MXP clients, each in its own dialect.

You can also send data encapsulated in a JSON object.

See [json()] for information about formatting data into JSON object strings.<br>
See [wsjson()] for help sending formatted JSON object strings to WebSocket clients as a JavaScript object.

See [@prompt] for information about sending telnet GOAHEAD prompts. Support for prompts depends on the WebSocket client. The example client above shows prompts on their own line, separating the input and output windows, but requires PROMPT_NEWLINES to be turned off.


**See Also:**
- [HTML Functions]
- [json()]
- [pueblo]
- [wshtml()]
- [wsjson()]

# WSHTML()
# WSJSON()

`wshtml(<html string>[, <default string>])`<br>
`wsjson(<json string>[, <default string>])`

These functions are used to embed HTML and JSON markup into the output for WebSocket-enabled clients. You must have the Pueblo_Send power to use them.

**wshtml()** embeds *<html string>* as HTML markup, to be rendered as HTML by a WebSocket client.

**wsjson()** embeds *<json string>* as a JSON object which can be captured by a WebSocket client. See [json()] for information about formatting JSON object strings.

In both cases, the *<default string>* is shown as plain text if the recipient is not WebSocket-enabled.

For example, if one uses:

```sharp
@emit [wshtml(<a href="https://sharpmush.com">SharpMUSH</a>,Go to https://sharpmush.com)]
```

then any players in the room with a WebSocket connection would see (rendered as HTML)

```html
<a href="https://sharpmush.com">SharpMUSH</a>
```

while non-WebSocket connections and listening objects would see

```text
Go to https://sharpmush.com
```


**See Also:**
- [WebSockets]
- [Pueblo]
- [HTML Functions]
- [JSON Functions]