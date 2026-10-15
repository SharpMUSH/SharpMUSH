# gmcp-comm

Sends MUD clients the GMCP **Comm** package, so a client such as Mudlet can keep channel lines and
pages in a chat window of their own. Part of the MUD-kit.

## What it sends

| Package | Body | When |
|---|---|---|
| `Comm.Channel.Text` | `{"channel":"Public","talker":"Wren","text":"<Public> Wren says, \"Hi.\""}` | A channel line or page reached the player |
| `Comm.Channel.List` | `[{"name":"Public","caption":"Public","command":"+public"}]` | The player connects, joins or leaves a channel |

- `text` is the line as the player's terminal shows it, in ANSI colour (`render(..., ansi)`), which Mudlet's chat window draws. A connection whose client says it is a screen reader (`terminfo()` lists `screenreader`) is sent it without colour.
- A page is filed under the channel the `page-channel` setting names (`page`): the recipient's copy reads `Wren pages: Hi.`, the pager's `You paged Bo with 'Hi.'`, poses as `From afar, ...` and `Long distance to ...`.
- `command` is what a client types before a message to speak on the channel.

The engine decides who received what. `` CHANNEL`MESSAGE `` names exactly the members a line was delivered
to, and `` PAGE`MESSAGE `` exactly the players a page reached, so a line goes to those players and no one
else; the package never works out channel or page permissions again.

Every send is `oob(..., supported)`. **Mudlet does not list `Comm` in `Core.Supports`**, so a Mudlet
script that wants these adds it:

```lua
sendGMCP([[Core.Supports.Add ["Comm.Channel 1"] ]])
```

A client that lists nothing (TinTin++, MUSHclient) is sent everything. comm-feed, which feeds the web
portal's Play sidebar, is a different package with its own shapes.

## Events

On the configured event handler: `` CHANNEL`MESSAGE`GMCPCOMM ``, `` PAGE`MESSAGE`GMCPCOMM `` and
`` PLAYER`CHANNELS`GMCPCOMM `` (which fires on connect and on every change to a player's channel list).
The payloads are `` FN`GMCPCOMM`* `` on the event handler.

## Settings: `+config gmcp-comm`

| Setting | Default | What |
|---|---|---|
| `pages` | on | Whether pages are sent too |
| `page-channel` | `page` | The channel name pages are filed under |
