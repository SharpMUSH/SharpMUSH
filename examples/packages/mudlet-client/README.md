# mudlet-client

Mudlet's own GMCP packages: the game's interface package (`Client.GUI`), its map (`Client.Map`), and
Mudlet's Discord rich presence (`External.Discord.Info` and `Status`), plus a `+gui` command. Part of
the MUD-kit.

## What it sends

| Package | Body | When |
|---|---|---|
| `Client.Map` | `{"url":"https://example.org/game-map.xml"}` | Mudlet says `Core.Hello` |
| `Client.GUI` | `{"version":"1.0","url":"https://example.org/game.mpackage"}` | The player connects, unless they turned `+gui off`; `Core.Hello` from a connection already logged in; `+gui on` |
| `External.Discord.Info` | `{"inviteurl":"https://discord.gg/abc","applicationid":"1234"}` | Mudlet says `External.Discord.Hello` |
| `External.Discord.Status` | `{"game":"My Game","details":"Playing Wren","starttime":1792000000}` | `External.Discord.Hello` and `Get`, and when the player connects |

Mudlet says `Core.Hello` before anyone has logged in, when no player's choice is known yet, so
`Client.GUI` waits for the login: that is what makes `+gui off` hold. A URL or id the settings leave
empty is not sent, and before login the Discord status names only the game.

Every send is `oob(..., supported)`. Mudlet lists `External.Discord` in `Core.Supports`, but not
`Client.GUI` or `Client.Map`: once Mudlet has sent its list, `supported` keeps the Client packages
from it unless a script adds `Client` (`sendGMCP([[Core.Supports.Add ["Client 1"] ]])`). The list is
usually recorded before `` PLAYER`CONNECT `` runs, so a stock Mudlet is offered the map at `Core.Hello`
at best. This is the cost of sending every package through `supported`; see the MUD-kit notes.

## +gui

| Command | What |
|---|---|
| `+gui` | Whether the game has an interface package, its version, and whether it is offered to you |
| `+gui on` | Offer it when you connect, and offer it now |
| `+gui off` | Stop offering it |

A screen reader (`terminfo()` lists `screenreader`) is told in two plain sentences instead of a box.
Turning it off is kept on the Mudlet Client object as `` DATA`GUI-OFF`<key> ``, the objid with `#` dropped and `:` as `-`.

## Objects and events

The **Mudlet Client** in the master room carries `+gui`, the settings, the help and every function that
builds and sends a package. It holds the `Send_OOB` power, which lets `oob()` reach connections that are
not its own, and nothing more. The event handler carries `` SOCKET`GMCP`MUDLETCLIENT `` (Core.Hello,
External.Discord.Hello and External.Discord.Get) and `` PLAYER`CONNECT`MUDLETCLIENT ``, each one line calling it.

## Settings: `+config mudlet-client`

| Setting | Default | What |
|---|---|---|
| `gui-url` | (none) | The interface package Mudlet downloads |
| `gui-version` | `1.0` | Its version; Mudlet installs it again when this changes |
| `map-url` | (none) | The map Mudlet downloads |
| `discord-invite` | (none) | An invite to the game's Discord server |
| `discord-application` | (none) | The game's own Discord application id |
| `discord-game` | the game's name | The game's name in a Discord status |
| `discord-character` | on | Whether the status names the character played |
