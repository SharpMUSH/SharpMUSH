# gmcp-room

Sends MUD clients the GMCP **Room** package, so a client such as Mudlet can draw a map and a list of
who is here. Part of the MUD-kit; `mud-kit` installs it with the others.

## What it sends

The shapes are Iron Realms', which Mudlet's mapper scripts read and TelnetNegotiationCore's `RoomInfo`
type writes.

| Package | Body | When |
|---|---|---|
| `Room.Info` | `{"num":1234,"name":"Garden","area":"Town","environment":"garden","coords":"3,4,0","exits":{"North":1235}}` | The player moves, or connects (on the new connection) |
| `Room.Players` | `[{"name":"Wren","fullname":"Wren of the Marsh"}]` | With every `Room.Info` |
| `Room.AddPlayer` | `{"name":"Wren","fullname":"Wren of the Marsh"}` | To the others, when a connected player arrives or connects |
| `Room.RemovePlayer` | `"Wren"` | To the others, when a player leaves or their last connection closes |

- `num`, and the room each exit leads to, are dbref numbers: stable while the room exists, as a mapper wants.
- `exits` is `exitmap(<room>, <player>)`: the exits the player sees, as `look` shows them. The package never decides exit visibility itself.
- `area` is the room's area attribute, or else its zone's name. `coords` is left out of a room with no coordinates.
- `Room.Players` lists the connected players the viewer sees, the viewer included. A DARK player is seen only by themself and by someone who may see everything.

Every send is `oob(..., supported)`: a client that listed GMCP modules in `Core.Supports` without
`Room` gets none of it, and one that listed nothing gets all of it. Mudlet lists `Room`.

## Events

All on the configured event handler (`{{$event_handler}}`, `#9` by default), one level below each event:

- `` OBJECT`MOVE`GMCPROOM ``: a connected player moved. Info and Players to them, RemovePlayer to the room they left, AddPlayer to the room they reached.
- `` PLAYER`CONNECT`GMCPROOM ``: Info and Players on the new descriptor; AddPlayer to the room on the first connection.
- `` PLAYER`DISCONNECT`GMCPROOM ``: RemovePlayer to the room when the last connection closes.

The payloads are built by `` FN`GMCPROOM`* `` on the event handler, which runs as a wizard so `exitmap()` may map the
room as the player standing in it.

## Settings: `+config gmcp-room`

| Setting | Default | What |
|---|---|---|
| `area` | `AREA` | The room attribute naming its area; a room without it is in its zone's area |
| `environment` | `ENVIRONMENT` | The room attribute holding its terrain |
| `coordinates` | `X Y Z` | The room attributes holding x, y and z |
| `fullname` | `FULLNAME` | The player attribute holding the name shown in full |

The values are on the **GMCP Room** object, which also carries the `+help gmcp room` topic.
