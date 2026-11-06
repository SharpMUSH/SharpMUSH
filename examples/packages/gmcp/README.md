# gmcp

Sends MUD clients the standard GMCP modules, in the Iron Realms shapes Mudlet reads and
TelnetNegotiationCore's types write: **Room** for a mapper and who is here, **Comm** for a chat window,
**Char.Items** for an inventory window and **Char** for a name, gauges and status. One install, one
settings table, one `+help gmcp`.

What a single client adds of its own is an add-on on top of this package:
[`gmcp-mudlet`](../gmcp-mudlet/) for Mudlet's interface package, map and Discord rich presence.

## What it sends

| Module | Package | Body | When |
|---|---|---|---|
| Room | `Room.Info` | `{"num":1234,"hash":"#1234:1761800000","name":"Garden","area":"Town","environment":"garden","coords":"3,4,0","exits":{"North":1235}}` | The player moves, or connects (on the new connection) |
| Room | `Room.Players` | `[{"name":"Wren","fullname":"Wren of the Marsh"}]` | With every `Room.Info` |
| Room | `Room.AddPlayer` | `{"name":"Wren","fullname":"Wren of the Marsh"}` | To the others, when a connected player arrives or connects |
| Room | `Room.RemovePlayer` | `"Wren"` | To the others, when a player leaves or their last connection closes |
| Comm | `Comm.Channel.Text` | `{"channel":"Public","talker":"Wren","text":"<Public> Wren says, \"Hi.\""}` | Each channel line and page, to exactly the players the engine delivered it to |
| Comm | `Comm.Channel.List` | `[{"name":"Public","caption":"Public","command":"+public"}]` | On connect, and whenever the player's channels change |
| Items | `Char.Items.List` | `{"location":"inv","items":[{"id":"1234","name":"a lantern"}]}` | On connect, when the client sends `Char.Items.Inv` or `Char.Items.Room`, and the room's things when the player moves |
| Items | `Char.Items.Add` / `Remove` | `{"location":"room","item":{"id":"1234","name":"a lantern"}}` | A thing reaches or leaves the player's hands or room |
| Char | `Char.Name` | `{"name":"Wren","fullname":"Wren of the Marsh"}` | On connect and rename |
| Char | `Char.Vitals` | `{"hp":25,"maxhp":40,"mp":10,"maxmp":12}` | On connect, and when one of its attributes changes |
| Char | `Char.Status` | `{"name":"Wren","fullname":"Wren of the Marsh","level":3}` | On connect, on rename, and when one of its attributes changes |

- `num`, each exit's room and each item's `id` are dbref numbers: stable while the object exists.
- `hash` is the place a room stands for on a stored map (its `` MAP`PLACE ``), or else its objid, so a mapper that keys rooms by it, as Carto does, keeps one room per place.
- `exits` is `exitmap(<room>, <player>)`: the exits the player sees, as `look` shows them, plus the ways a world map's zone lends the room (`` ZONE`EXITS ``).
- `Room.Players` lists the connected players the viewer sees, the viewer included. A DARK player or thing is seen only by itself and by someone who may see everything.
- `Comm.Channel.Text`'s `text` is the line in ANSI colour, or plain for a client that says it is a screen reader.

Every send is `oob(..., supported)`: a client that listed GMCP modules in `Core.Supports` is sent only
those, and one that listed nothing is sent everything. Mudlet lists `Room` and `Char` but not `Comm`; a
Mudlet script adds it with `sendGMCP([[Core.Supports.Add ["Comm.Channel 1"] ]])`.

## Char and @attribute/watch

Nothing polls. For each attribute the `vitals` and `status` settings name, the **GMCP** object (at install,
upgrade, and when `+config` changes either setting) adds it to the attribute table when it is not there,
watches it with `@attribute/watch`, and copies `` TEMPLATE`GMCP`WATCH `` to `` WATCH`<attribute>`GMCP `` on the
event handler. An attribute already watched with another pattern is left alone, not sent, and listed in
`` DATA`FOREIGN ``. Each handler removes itself once the package is uninstalled.

## Objects and events

- The event handler (`{{$event_handler}}`, `#9` by default) carries the handlers, one level below each
  event: `` OBJECT`MOVE`GMCP ``, `` PLAYER`CONNECT`GMCP ``, `` PLAYER`DISCONNECT`GMCP ``,
  `` CHANNEL`MESSAGE`GMCP ``, `` PAGE`MESSAGE`GMCP ``, `` PLAYER`CHANNELS`GMCP ``, `` SOCKET`GMCP`GMCP ``,
  `` OBJECT`RENAME`GMCP `` and the `` WATCH`<attribute>`GMCP `` copies. The payloads are built by
  `` FUN`GMCP`* ``, shared helpers first and then one section per module, running as the event handler so
  `exitmap()` may map a room as the player standing in it.
- **GMCP** (WIZARD, for `@attribute` and the watch handlers) carries the settings and the watching.
- **GMCP Help** carries the `+help gmcp` topics.

## Settings: `+config gmcp`

| Setting | Default | What |
|---|---|---|
| `modules` | `Room Comm Items Char` | The modules the game sends at all |
| `fullname` | `FULLNAME` | The player attribute holding the name shown in full, in Room.Players and Char.Name |
| `area` | `AREA` | The room attribute naming its area; a room without it is in its zone's area |
| `environment` | `ENVIRONMENT` | The room attribute holding its terrain |
| `coordinates` | ``MAP`COORDS`X MAP`COORDS`Y MAP`COORDS`Z`` | The room attributes holding x, y and z, the ones the World map reads |
| `pages` | on | Whether pages are sent as Comm.Channel.Text |
| `page-channel` | `page` | The channel a page is filed under |
| `items-room` | on | Whether the things lying in the room are sent, or only what the player carries |
| `vitals` | `HP MAXHP MP MAXMP` | The player attributes sent as Char.Vitals |
| `status` | (none) | The player attributes sent as Char.Status |

[`gmcp-help`](../gmcp-help/) and [`gmcp-config`](../gmcp-config/) register the topics and the settings with
`+help` and `+config` wherever those are installed.
