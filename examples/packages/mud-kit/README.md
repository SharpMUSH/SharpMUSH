# mud-kit

Everything a MUD client such as Mudlet reads over GMCP, in one install. A meta package: it depends on
the five below and adds a `+help gmcp` overview. Each of the five installs and uninstalls on its own,
so a game takes only what it uses.

| Package | Sends | Driven by |
|---|---|---|
| [`gmcp-room`](../gmcp-room/) | Room.Info, Room.Players, Room.AddPlayer, Room.RemovePlayer | `` OBJECT`MOVE ``, `` PLAYER`CONNECT ``, `` PLAYER`DISCONNECT `` |
| [`gmcp-comm`](../gmcp-comm/) | Comm.Channel.Text, Comm.Channel.List | `` CHANNEL`MESSAGE ``, `` PAGE`MESSAGE ``, `` PLAYER`CHANNELS `` |
| [`gmcp-items`](../gmcp-items/) | Char.Items.List, Add, Remove | `` OBJECT`MOVE ``, `` PLAYER`CONNECT ``, `` SOCKET`GMCP `` (Char.Items.Inv and Room) |
| [`gmcp-char`](../gmcp-char/) | Char.Name, Char.Vitals, Char.Status | `` WATCH`<attribute>`GMCPCHAR `` for the attributes its settings name, which it watches with `@attribute/watch`; `` PLAYER`CONNECT ``, `` OBJECT`RENAME `` |
| [`mudlet-client`](../mudlet-client/) | Client.GUI, Client.Map, External.Discord.Info and Status; `+gui` | `` SOCKET`GMCP `` (Core.Hello, External.Discord.Hello and Get), `` PLAYER`CONNECT `` |

## How they fit together

- **The engine owns the protocol.** It negotiates GMCP, keeps each client's `Core.Supports` list
  (`gmcpsupports()`), and raises the events. The packages shape the game's data into the standard
  GMCP bodies and send them with `oob()`; the engine never builds a Room.Info itself.
- **Sends go through `oob(..., supported)`.** A client that listed modules in `Core.Supports` is sent only
  what it listed (or a module it belongs to: `Room` covers `Room.Info`); one that listed nothing, such
  as TinTin++, Blightmud or MUSHclient, is sent everything. mudlet-client's `Client.GUI` and
  `Client.Map` are the exception: Mudlet never lists `Client`, so they go to every GMCP connection.
- **Event handlers, one level below the event.** Each package adds `` <EVENT>`<PACKAGE> `` attributes to the
  configured event handler, beside room-contents', comm-feed's and the game's own, and never edits
  another's (`help event`).
- **Settings are `+config`.** Each package declares its settings on its own object and registers it
  with plus-config: `+config gmcp-room`, `+config gmcp-char`, and so on.
- **Screen readers.** Text a client shows in a window of its own (a chat line) is sent without colour to
  a connection whose client says it is a screen reader, and `+gui` speaks in sentences rather than a box.

## Notes for Mudlet

Mudlet's `Core.Supports` list is `Char`, `Char.Skills`, `Char.Items`, `Room`, `IRE.Rift`,
`IRE.Composer`, `External.Discord` and `Client.Media`. A stock Mudlet that has sent its list is
therefore not sent `Comm.*` until a script adds it (the game's interface package is the place):

```lua
sendGMCP([[Core.Supports.Add ["Comm.Channel 1"] ]])
```
