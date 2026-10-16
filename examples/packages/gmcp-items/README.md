# gmcp-items

Sends MUD clients the GMCP **Char.Items** package: what a player carries and what lies in their room,
for an inventory window. Part of the MUD-kit.

## What it sends

The shapes are Iron Realms', as TelnetNegotiationCore's `ItemsList` and `ItemChange` types write them.

| Package | Body | When |
|---|---|---|
| `Char.Items.List` | `{"location":"inv","items":[{"id":"1234","name":"a lantern"}]}` | On connect (inv and room), when the client asks, and the room's list when the player moves |
| `Char.Items.Add` | `{"location":"room","item":{"id":"1234","name":"a lantern"}}` | A thing reached the player's hands (`inv`) or their room (`room`) |
| `Char.Items.Remove` | `{"location":"inv","item":{"id":"1234","name":"a lantern"}}` | A thing left them |

A client asks with `Char.Items.Inv` or `Char.Items.Room`, which arrive as `` SOCKET`GMCP `` and are answered on
the descriptor that asked. `id` is the thing's dbref number as a string of digits. Things only: players
are in gmcp-room's `Room.Players`, exits in its `Room.Info`. A DARK thing in a room is listed only to a
player who may see everything. Containers (`Char.Items.Contents`) are not answered.

Every send is `oob(..., supported)`. Mudlet lists `Char.Items`.

## Events

On the configured event handler: `` OBJECT`MOVE`GMCPITEMS `` (a player moved, or a thing moved between a player and a room),
`` PLAYER`CONNECT`GMCPITEMS `` and `` SOCKET`GMCP`GMCPITEMS ``. The payloads are `` FUN`GMCPITEMS`* `` on the event handler.

## Settings: `+config gmcp-items`

| Setting | Default | What |
|---|---|---|
| `room` | on | Whether the room's things are sent, or only the inventory |
