# gmcp-mudlet

Mudlet's own GMCP packages: an interface package (`Client.GUI`), the game's map (`Client.Map`), and
Mudlet's Discord rich presence (`External.Discord.Info` and `Status`), plus a `+gui` command. An add-on to
[`gmcp`](../gmcp/), which sends the standard modules every client reads; this package adds only what
is Mudlet's own.

## The interface

An **interface package** is a Mudlet package (`.mpackage`: a zip of Lua scripts and their settings) that
draws the game's own windows inside Mudlet. The game sends its address with `Client.GUI`; Mudlet
downloads and installs it, and installs it again when the version it is sent changes, unless the
player turned off Mudlet's *Allow server to install script packages* preference.

Out of the box the game offers the one the server ships, served at
`<mud_url>/mudlet/sharpmush-ui.mpackage`. It puts a panel on the right of the game's text, filled from
what `gmcp` sends:

| Part | Drawn from |
|---|---|
| The room: name, area, exits, who is here, things here, things carried | `Room.Info`, `Room.Players`, `Char.Items.*` |
| A gauge for each value sent with a maximum (`hp` and `maxhp`) | `Char.Vitals` |
| A chat window for channel lines and pages | `Comm.Channel.Text` |
| A map, drawn as the player walks: rooms placed by compass exit, or at `coords` when the game sends them | `Room.Info` |

A part the game sends nothing for stays empty. The package also adds `Comm.Channel` to Mudlet's
`Core.Supports`, which a stock Mudlet leaves out, so the chat window is sent its lines. In Mudlet,
`interface hide` and `interface show` take the panel away and bring it back.

It needs `mud_url` to be the game's web address (setup fills it in). Its source is
`SharpMUSH.Server/Mudlet/`: `config.lua` and `sharpmush-ui.lua`, which the server zips with a Mudlet XML
file around the script.

To offer your own interface instead, download the shipped one, change it in Mudlet, export it from
Mudlet's Package Exporter under a name of your own, put it on a web server, and set `gui-url` to its
address and `gui-version` to its version. Mudlet names an installed package after the file name in the
URL, so keep the file name and change `gui-version` with each new file. `gui` off offers none.

## What it sends

| Package | Body | When |
|---|---|---|
| `Client.Map` | `{"url":"https://example.org/game-map.xml"}` | Mudlet says `Core.Hello` |
| `Client.GUI` | `{"version":"1.0.0","url":"https://example.org/mudlet/sharpmush-ui.mpackage"}` | The player connects, unless they turned `+gui off`; `Core.Hello` from a connection already logged in; `+gui on` |
| `External.Discord.Info` | `{"inviteurl":"https://discord.gg/abc","applicationid":"1234"}` | Mudlet says `External.Discord.Hello` |
| `External.Discord.Status` | `{"game":"My Game","details":"Playing Wren","starttime":1792000000}` | `External.Discord.Hello` and `Get`, and when the player connects |

Mudlet says `Core.Hello` before anyone has logged in, when no player's choice is known yet, so
`Client.GUI` waits for the login: that is what makes `+gui off` hold. A map URL or Discord id the
settings leave empty is not sent, and before login the Discord status names only the game. The Discord
packages do not depend on the interface: they answer Mudlet's `External.Discord.Hello` and `Get`,
which Mudlet sends when it connects, and only while Discord runs on the player's computer.

The Discord packages are sent with `oob(..., supported)`; Mudlet lists `External.Discord`.
`Client.GUI` and `Client.Map` are sent with plain `oob()`, to every connection that takes GMCP:
Mudlet never lists `Client` in `Core.Supports`, and the script that would add it lives in the
interface package being offered, so filtering them would keep them from every stock Mudlet. A client
that does not know `Client.*` ignores it.

## +gui

| Command | What |
|---|---|
| `+gui` | Whether the game offers an interface package, whose it is, its version, and whether it is offered to you |
| `+gui on` | Offer it when you connect, and offer it now |
| `+gui off` | Stop offering it |

A screen reader (`terminfo()` lists `screenreader`) is told in two plain sentences instead of a box.
Turning it off is kept on the Mudlet Client object as `` DATA`GUI-OFF`<key> ``, the objid with `#` dropped and `:` as `-`.

## Objects and events

The **Mudlet Client** in the master room carries `+gui`, the settings, the help and every function that
builds and sends a package. It holds the `Send_OOB` power, which lets `oob()` reach connections that are
not its own, and nothing more. The event handler carries `` SOCKET`GMCP`GMCPMUDLET `` (Core.Hello,
External.Discord.Hello and External.Discord.Get) and `` PLAYER`CONNECT`GMCPMUDLET ``, each one line calling it.

## Settings: `+config gmcp-mudlet`

| Setting | Default | What |
|---|---|---|
| `gui` | on | Whether Mudlet is offered an interface at all |
| `gui-url` | (none) | The game's own interface package; empty offers the one the server ships |
| `gui-version` | `1.0` | The version of the package `gui-url` names; Mudlet installs it again when this changes |
| `map-url` | (none) | The map Mudlet downloads |
| `discord-invite` | (none) | An invite to the game's Discord server |
| `discord-application` | (none) | The game's own Discord application id |
| `discord-game` | the game's name | The game's name in a Discord status |
| `discord-character` | on | Whether the status names the character played |
