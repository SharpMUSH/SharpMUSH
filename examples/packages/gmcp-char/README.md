# gmcp-char

Sends MUD clients the GMCP **Char** package: the character's name, the vitals a client draws as gauges,
and their status, each sent again the moment one of its attributes changes. Part of the MUD-kit.

## What it sends

| Package | Body | When |
|---|---|---|
| `Char.Name` | `{"name":"Wren","fullname":"Wren of the Marsh"}` | On connect, and when the player is renamed |
| `Char.Vitals` | `{"hp":25,"maxhp":40,"mp":10,"maxmp":12}` | On connect, and when a vitals attribute changes |
| `Char.Status` | `{"name":"Wren","fullname":"Wren of the Marsh","level":3}` | On connect, when a status attribute changes, and on a rename |

Each attribute is sent under its last name in lower case (`STATS`HP` is `hp`), as a number when it is
one. An attribute the player does not have is left out. These are the keys Mudlet's gauges read, as
TelnetNegotiationCore's `CharVitals` type writes them.

Every send is `oob(..., supported)`. Mudlet lists `Char`.

## No polling: watched attributes

The engine raises `` ATTRIBUTE`CHANGE `` only for an attribute flagged `watched`
(`help attribute flags`), so an attribute nobody watches costs nothing. The package flags the
attributes its `vitals` and `status` settings name:

1. `@attribute/access <attribute>=watched`, so every copy made from then on is watched, when the
   `watch-new` setting is on (the default). **This replaces the attribute's default flags.** A game
   whose stat attributes are already standard attributes with flags of their own turns `watch-new` off,
   or writes `@attribute/access HP=watched <its flags>` itself.
   Turning `watch-new` off, or dropping an attribute from `vitals` or `status`, puts the defaults
   the package gave back to none.
2. `@set <player>/<attribute>=watched` on every player who already has one, from `AINSTALL`, `AUPDATE`,
   and `ACONFIG` when `vitals`, `status` or `watch-new` changes (`+config` triggers it).
3. The same on each player as they connect (`` PLAYER`CONNECT`GMCPCHAR ``), which catches a copy made
   while the attribute was not watched.

To watch one by hand: `@set <player>/HP=watched`. Uninstalling leaves the flags; they cost nothing
without a handler.

The **GMCP Char** object is `WIZARD` because `@attribute/access` and `@set` on other players need it,
and it runs nothing but those. Its help is on the separate **GMCP Char Help** object, because the
`+help` librarian cannot read a privileged object's topics.

## Events

On the configured event handler: `` ATTRIBUTE`CHANGE`GMCPCHAR ``, `` PLAYER`CONNECT`GMCPCHAR `` and
`` OBJECT`RENAME`GMCPCHAR ``. The payloads are `` FN`GMCPCHAR`* `` on the event handler.

## Settings: `+config gmcp-char`

| Setting | Default | What |
|---|---|---|
| `vitals` | `HP MAXHP MP MAXMP` | The attributes sent as `Char.Vitals` |
| `status` | (none) | The attributes sent as `Char.Status`, such as `LEVEL CLASS` |
| `fullname` | `FULLNAME` | The attribute holding the name shown in full |
| `watch-new` | on | Whether `@attribute/access` watches every new copy |
