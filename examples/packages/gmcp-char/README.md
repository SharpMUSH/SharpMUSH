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

Nothing polls. `@attribute/watch` makes the engine run the event handler's `` WATCH`<attribute>`* ``
attributes whenever any copy of a watched attribute is set, cleared or wiped (`help @attribute watch`),
so an attribute nobody watches costs nothing. For each attribute its `vitals` and `status` settings name,
the package, at install, on an upgrade and whenever `+config` changes `vitals` or `status`:

1. Adds it to the attribute table with `@attribute/access <attribute>=none`, but only when `attrwatch()`
   says it is not there. An entry the game already has keeps its flags.
2. Watches it with `@attribute/watch <attribute>` when it is not watched yet, which gives it the default
   pattern `` WATCH`<attribute> WATCH`<attribute>`* ``.
3. Puts its handler, `` WATCH`<attribute>`GMCPCHAR ``, on the event handler. It sends `Char.Vitals` or
   `Char.Status` to the player whose attribute changed.

An attribute that is already watched with another pattern belongs to another package or to staff. The
package leaves it as it is and gives it no handler, so its changes are not sent; whoever changed the
setting is told, and the GMCP Char object's `` DATA`FOREIGN `` lists it.

An attribute dropped from `vitals` or `status` loses its `` WATCH`<attribute>`GMCPCHAR ``. It stops being
watched (`@attribute/watch <attribute>=`) only when its pattern is still the default and nothing else
is left under `` WATCH`<attribute> `` on the event handler, so another package's `` WATCH`HP`GAUGE `` keeps
HP watched. Its table entry stays.

**Uninstalling** runs no softcode, so the package manager cannot remove the handlers it made. Each
handler checks that the package is still installed when it runs: if not, it removes itself and stops
the watch the same way. Until an attribute next changes, its handler sits there doing nothing.

The handlers are made at run time because their names come from the settings. That is also why the
manifest has no format 1.9 `watch:` entries: those would name the attributes in the manifest, and
would make the package own each table entry, so uninstalling would delete a game's `HP` entry.

The **GMCP Char** object is `WIZARD` because `@attribute` needs config.admin and the handlers go on the
event handler, and it runs nothing but those. Its help is on the separate **GMCP Char Help** object,
because the `+help` librarian cannot read a privileged object's topics.

## Events

On the configured event handler: `` PLAYER`CONNECT`GMCPCHAR `` and `` OBJECT`RENAME`GMCPCHAR ``, and one
`` WATCH`<attribute>`GMCPCHAR `` for each attribute it watches, copied from `` FN`GMCPCHAR`HANDLER ``. The
payloads are `` FN`GMCPCHAR`* `` on the event handler.

## Settings: `+config gmcp-char`

| Setting | Default | What |
|---|---|---|
| `vitals` | `HP MAXHP MP MAXMP` | The attributes sent as `Char.Vitals` |
| `status` | (none) | The attributes sent as `Char.Status`, such as `LEVEL CLASS` |
| `fullname` | `FULLNAME` | The attribute holding the name shown in full |
