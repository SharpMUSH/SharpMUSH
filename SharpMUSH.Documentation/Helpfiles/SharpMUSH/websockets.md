<!-- help-article
{
  "corpus": "help",
  "id": "websockets",
  "lookup": "WebSockets",
  "aliases": [],
  "sections": [
    {"id": "markup", "heading": "Kinds of markup", "lookup": "websockets markup"},
    {"id": "json-prompts", "heading": "JSON and prompts", "lookup": "websockets json and prompts"}
  ]
}
-->
# WebSockets

WebSockets are a network protocol used by JavaScript-enabled web browsers to make persistent network connections, similar to the telnet connection you use to connect to SharpMUSH. With WebSockets enabled in mush.cnf, it is possible to connect from MUSH clients embedded in HTML pages using JavaScript. A WebSocket client can natively render HTML, but can also parse Pueblo links into HTML links that send a command to the MUSH when clicked. For safety, we separate plain text from the other kinds of HTML/Pueblo code that we want rendered. In order to render HTML/Pueblo, a player with the Pueblo_Send power uses special functions to embed HTML/Pueblo markup. Players without the PUEBLO_SEND power can not use these markup functions. Any HTML code strings that are not properly marked up will simply show up as unrendered plain text.

See https://github.com/grapenut/websockclient for an example client.

- [websockets markup]: plain text, HTML tags and command links
- [websockets json and prompts]: JSON objects and prompts

## Kinds of markup

The different kinds of markup that can be sent to clients are:
* Plain text
* HTML tags
* Pueblo links
* JSON objects
* Prompts

Without using any HTML markup functions, output is rendered as normal plain text (including ANSI and xterm256 color).

See [HTML FUNCTIONS] for functions used to embed HTML markup tags one at a time.<br>
See [WSHTML()] for help embedding large segments of raw HTML markup to be sent to WebSocket clients.

For clickable command links, use [CMDLINK()], e.g. `cmdlink(Who is online?,+who)`. The web portal and the example client above both follow it, and so do Pueblo and MXP clients, each in its own dialect.

## JSON and prompts

You can also send data encapsulated in a JSON object.

See [json()] for information about formatting data into JSON object strings.<br>
See [OOB()] for sending a JSON object to a WebSocket client as a JavaScript object, or to a telnet client over GMCP.

See [@prompt] for information about sending telnet GOAHEAD prompts. Support for prompts depends on the WebSocket client. The example client above shows prompts on their own line, separating the input and output windows, but requires PROMPT_NEWLINES to be turned off.

::: seealso
- [HTML FUNCTIONS]
- [json()]
- [pueblo]
- [WSHTML()]
- [OOB()]
:::
