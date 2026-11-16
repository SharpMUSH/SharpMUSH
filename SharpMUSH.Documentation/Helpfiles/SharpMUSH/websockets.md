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

WebSockets are a network protocol used by JavaScript-enabled web browsers to make persistent network connections, similar to the telnet connection you use to connect to SharpMUSH. WebSockets are always on (at /ws on the connection server), which is how the web portal's terminal connects. Messages on /ws are JSON objects whose `type` names what they carry, such as `markup` for a line of output, `json` for [WSJSON()] and `oob` for [OOB()]. This is not PennMUSH's WebSocket protocol, so a client written for PennMUSH's /wsclient cannot use it. For safety, we separate plain text from the other kinds of HTML/Pueblo code that we want rendered. In order to render HTML/Pueblo, a player uses special functions to embed HTML/Pueblo markup. Without the Send_OOB power (or wizard), these functions allow only a safe list of tags (see [TAGWRAP()]). Any HTML code strings that are not properly marked up will simply show up as unrendered plain text.

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

For clickable command links, use [CMDLINK()], e.g. `cmdlink(Who is online?,+who)`. The web portal follows it, and so do Pueblo and MXP clients, each in its own dialect.

## JSON and prompts

You can also send data encapsulated in a JSON object.

See [json()] for information about formatting data into JSON object strings.<br>
See [WSJSON()] for embedding a JSON object in a line of output, with plain text for every other reader.<br>
See [OOB()] for sending a JSON object to a WebSocket client as a JavaScript object, or to a telnet client over GMCP.

See [@prompt] for information about sending telnet GOAHEAD prompts. The web portal shows a prompt in its own row below the output.

::: seealso
- [HTML FUNCTIONS]
- [json()]
- [pueblo]
- [WSHTML()]
- [WSJSON()]
- [OOB()]
:::
