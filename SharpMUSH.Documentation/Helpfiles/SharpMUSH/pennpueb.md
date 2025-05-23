# PUEBLO

Pueblo is a client made by Chaco (a now defunct company). It attempts to mix HTML with MUSH. There are other clients (notably MUSHclient) that also offer Pueblo features. SharpMUSH can offer support for some of the enhanced features of Pueblo, enabled via the 'pueblo' @config option.

SharpMUSH will automatically detect a Pueblo client (rather, the client will announce itself and SharpMUSH will detect that), and set up that connection for Pueblo use. 

See also: [help pueblo2|pueblo2]

# PUEBLO2

SharpMUSH makes the following enhancements visible to Pueblo users when Pueblo support is enabled:

* Object/Room names are highlighted
* Support for VRML graphics
* Unordered list for contents and transparent exits
* Contents and exits lists have links (Click an exit to walk through it)
* Object lists (like the ones found in 'examine'/'inventory') have links
* Conversion of accented characters into &entity; codes

While Pueblo brings a number of new features and markups to MUSHes, in many ways it's not well suited. Because it's based on HTML, multiple spaces are compressed, and Pueblo typically defaults to a variable width font. Because of this, supporting Pueblo is not just a matter of enabling the option. The output of any commands which rely on fixed spacing, such as a +who, must be wrapped in `<pre>` tags to ensure they appear correctly for players using Pueblo. For instance:

```
> &cmd`who Globals=$+who: @nspemit %#=tagwrap(pre, u(fun`who))
```

See also:
- [help pueblo()|pueblo()]
- [help HTML Functions|HTML Functions]

# HTML

Hyper Text Markup Language (http://www.w3.org)

The only HTML implementation supported by the MUSH is the one supported by Pueblo (see [help pueblo|pueblo] for more info). To utilize HTML, use one of the MUSH HTML Functions (see [help HTML Functions|HTML Functions] for a list).

HTML tags are stripped when sent to non-HTML capable players.

See also:
- [help HTML Functions|HTML Functions]
- [help PUEBLO|PUEBLO]
- [help html()|html()]

# PUEBLO()

`pueblo(<player|descriptor>)`

This function returns 1 if the given player or descriptor is currently Pueblo-enabled, and 0 otherwise. 

If used on a player/descriptor which is not connected, pueblo() returns #-1 NOT CONNECTED. Mortals can only give a *<descriptor>* for their own connections (but can give any *<player>* arg), while See_All objects can check any descriptor.

When used with a *<player>* argument, the most recently active connection is used if the *<player>* is logged in more than once.

See also:
- [help terminfo()|terminfo()]
- [help html()|html()]
- [help PUEBLO|PUEBLO]

# @VRML_URL
# VRML_URL
# VRML

`@vrml_url <object>[=<url>]`

The VRML_URL attribute provides an object (usually a room) with a VRML world. When someone using a Pueblo-enabled client looks at the object, the VRML World listed in @VRML_URL will be loaded.

Example:
```
> @vrml_url here=http://www.pennmush.org/pennmush.vrml
```

To learn about the VRML Format, have a look at the Pueblo Help, which mentions several good sites for learning.

See also:
- [help HTML|HTML]
- [help PUEBLO|PUEBLO]

# HTML FUNCTIONS

HTML Functions are used to output HTML tags to HTML capable users. These tags will be stripped by the system for anything non-HTML related. These functions are only available when Pueblo support is enabled (see '@config pueblo').

Available functions:
- html()
- tag()
- endtag()
- tagwrap()
- wshtml()

Examples:
```
> say html(a href="http://www.pennmush.org")SharpMUSH[html(/a)]
> say tag(a,href="http://www.pennmush.org")SharpMUSH[endtag(a)]
> say tagwrap(a,href="http://www.pennmush.org",SharpMUSH)
> say wshtml(<a href="http://www.pennmush.org">SharpMUSH</a>)
```

Each of these produces the HTML output:
```
<a href="http://www.pennmush.org">SharpMUSH</a>
```

Mortals are restricted in the tags they may use. Most standard HTML tags are ok; protocol-specific tags like SEND and XCH_CMD can only be sent by Wizards or those with the Send_OOB @power.

# HTML()

`html(<string>)`

This wizard-only function will output *<string>* as an HTML Tag.

Example:
```
> think html(b)Foo[html(/b)]
```

Will output (in HTML):
```
<b>Foo</b>
```

Non-wizards should see the tag(), endtag(), and tagwrap() functions, which are similar but can be used by mortals.

See also:
- [help PUEBLO|PUEBLO]
- [help HTML|HTML]
- [help HTML Functions|HTML Functions]

[... Previous content ...]

# TAG()

`tag(<name>[, <param1>[, ... , <paramN>]])`

This function outputs the named HTML/Pueblo tag with the given paramaters.

Example:
```
tag(img,src="http://www.pennmush.org/image.jpg",align="left",width="300")
```

Will output (in HTML):
```
<img src="http://www.pennmush.org/image.jpg" align="left" width="300">
```

See also:
- [help endtag()|endtag()]
- [help tagwrap()|tagwrap()]
- [help html()|html()]

# ENDTAG()

`endtag(<name>)`

Outputs a closing HTML/Pueblo tag for the named tag.

Example:
```
endtag(b)
```

Will output (in HTML):
```
</b>
```

See also:
- [help tag()|tag()]
- [help tagwrap()|tagwrap()]
- [help html()|html()]

# TAGWRAP()

`tagwrap(<name>[, <parameters>], <string>)`

This function outputs *<string>*, wrapped in the *<name>* HTML/Pueblo tag with the specified *<parameters>*.

Example:
```
tagwrap(a,href="http://download.pennmush.org",SharpMUSH Downloads)]
```

Will output (in HTML):
```
<a href="http://download.pennmush.org">SharpMUSH Downloads</a>
```

A particularly important use of this function is `tagwrap(pre, <string>)`. Because Pueblo works like an html browser, spaces and tabs are compressed to a single space. If you have code (a +who function, for example) that relies on exact spacing, surround its output with a tagwrap(pre,...) so that Pueblo will render it as "preformatted" text.

See also:
- [help tag()|tag()]
- [help endtag()|endtag()]
- [help html()|html()]

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

See [help HTML Functions|HTML Functions] for functions used to embed HTML markup tags one at a time.
See [help wshtml()|wshtml()] for help embedding large segments of raw HTML markup to be sent to WebSocket clients.

Support for Pueblo links depends on the WebSocket client, however the example client above supports xch_cmd for command links and xch_hint for tooltip text popups. For clickable command links, embed a link tag with the command to be executed in the "xch_cmd" attribute, e.g. `<a xch_cmd="+who">Who is online?</a>`.

You can also send data encapsulated in a JSON object.

See [help json()|json()] for information about formatting data into JSON object strings.
See [help wsjson()|wsjson()] for help sending formatted JSON object strings to WebSocket clients as a JavaScript object.

See [help @prompt|@prompt] for information about sending telnet GOAHEAD prompts. Support for prompts depends on the WebSocket client. The example client above shows prompts on their own line, separating the input and output windows, but requires PROMPT_NEWLINES to be turned off.

See also:
- [help HTML Functions|HTML Functions]
- [help json()|json()]
- [help pueblo|pueblo]
- [help wshtml()|wshtml()]
- [help wsjson()|wsjson()]

# WSHTML()
# WSJSON()

`wshtml(<html string>[, <default string>])`
`wsjson(<json string>[, <default string>])`

These functions are used to embed HTML and JSON markup into the output for WebSocket-enabled clients. You must have the Pueblo_Send power to use them.

**wshtml()** embeds *<html string>* as HTML markup, to be rendered as HTML by a WebSocket client.

**wsjson()** embeds *<json string>* as a JSON object which can be captured by a WebSocket client. See [help json()|json()] for information about formatting JSON object strings.

In both cases, the *<default string>* is shown as plain text if the recipient is not WebSocket-enabled.

For example, if one uses:

```
@emit [wshtml(<a href="http://pennmush.org">SharpMUSH</a>,Go to http://pennmush.org)]
```

then any players in the room with a WebSocket connection would see (rendered as HTML)

```
<a href="http://pennmush.org">SharpMUSH</a>
```

while non-WebSocket connections and listening objects would see

```
Go to http://pennmush.org
```

See also:
- [help WebSockets|WebSockets]
- [help Pueblo|Pueblo]
- [help HTML Functions|HTML Functions]
- [help JSON Functions|JSON Functions]