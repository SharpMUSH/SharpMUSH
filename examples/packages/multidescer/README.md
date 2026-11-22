# multidescer

`+desc`, `+wear` and `+remove`: more than one description per character, either whole descriptions swapped in
and out or pieces worn by slot and put together, plus `+shortdesc`.

```
> +desc winter=A tall woman bundled in grey wool.
[DESC] Saved the description 'winter'. +desc winter wears it.
> +desc winter
[DESC] You now wear winter.
```

- **Whole descriptions.** `+desc/save <name>=<text>` saves one, `+desc <name>` wears it, and `+desc/save <name>`
  saves the description you have now, code and all. The commands follow the RhostMUSH and PennMUSH
  multi-descers players already know.
- **Pieces.** `+desc/save <slot>/<name>=<text>` saves a piece in a slot. `+wear <name>` puts it on over what is
  worn, `+remove <name>` takes it off, and `+desc <name> + <name>` wears exactly those. A piece saved without a
  slot is in `whole`, so a whole description is a piece too.
- **Slots.** Staff set the slots every player starts with as a `+config` table: the order pieces come in, which
  slots share a paragraph, which slots a slot covers (a coat hides the shirt) and how many pieces may be worn in
  one at once. Players change their own with `+desc/addslot`, `+desc/slot <slot>/<setting>=<value>` and
  `+desc/delslot`; the first change gives them a copy of the game's list, and `+desc/resetslots` goes back to it.
- **Markers.** A whole description can hold a slot's place: `<face>` in it is where the pieces worn in `face`
  are read.
- **Outfits.** `+desc/outfit <name>` saves the pieces worn now; `+desc <outfit>` wears them.
- **Nothing lost.** A description written another way, such as with `@describe`, is saved as a piece before
  anything is worn over it: `original` the first time, `kept-<id>` after that.
- **Evaluated once.** What is typed is evaluated as the player and kept as `decompose()` of the result, so
  the piece shows as typed and is never run again. A piece saved from the description as it was keeps its code.
- **`+shortdesc <text>`** sets `SHORTDESC` on the character, a one-line look for other code to show.

Every change writes the character's `DESCRIBE` as one `u()` of each worn piece's `TEXT`, joined by a space or a
blank line, so `look`, `@descformat` and the portal read it as they always do and each piece is evaluated on
its own. Editing `DESCRIBE` by hand still works; the next `+desc`, `+wear` or `+remove` saves it as a piece
before writing over it.

## In the portal

The [`multidescer-app`](../multidescer-app/) package registers a portal page at `/apps/descs`, under Play. This
package serves its routes on the HTTP handler: `GET /http/descs/schema` and `/http/descs/data` with `?at=` (empty for
the list, `piece/<id>`, `outfit/<id>`, `slots`, `slot/<name>`, `guide`), and `POST /http/descs/act`. A button posts an `op` and the page's `target`;
the route runs the same routine the command would, with the signed-in character in place of the one typing, and
answers with what that routine said. Text written on the page is kept as written (`decompose()`), unless the player
ticks that it is code.

## Storage

On the character, under a root flagged `wizard` and `no_command` the first time the package writes it:

| Attribute | Holds |
|---|---|
| ``MULTIDESC`PIECE`<id>`NAME`` | The piece's name |
| ``MULTIDESC`PIECE`<id>`SLOT`` | Its slot |
| ``MULTIDESC`PIECE`<id>`TEXT`` | Its text, as code for `DESCRIBE` |
| ``MULTIDESC`PIECE`<id>`TYPED`` | `1` when the text was typed as plain text and is kept `decompose()`d; without it the text is code |
| ``MULTIDESC`OUTFIT`<id>`NAME`` | The outfit's name |
| ``MULTIDESC`OUTFIT`<id>`PIECES`` | Its piece ids |
| ``MULTIDESC`WORN`` | The piece ids worn now, in the order put on |
| ``MULTIDESC`TOP`PIECE``, ``MULTIDESC`TOP`OUTFIT`` | The highest id deleted of that kind, so an id is never given out twice |
| ``MULTIDESC`SLOT`<NAME>`` | The player's own slot, once they change one: its order, with `PARAGRAPH`, `COVERS` and `MAX` beneath |
| ``MULTIDESC`LAST`` | The `DESCRIBE` the package last wrote, to tell when it was edited by hand |
| `SHORTDESC` | The short description, as typed |

## Settings

Kept on the Multi-descer object. The `multidescer-config` bridge registers them with [`+config`](../plus-config/)
(`+config multidescer`):

| Setting | Type | Default | What it changes |
|---|---|---|---|
| `name-length` | integer, 1 to 60 | 30 | The longest name of a piece or outfit |
| `length` | integer | 8000 | The longest text of one piece; the game's attribute limit caps it |
| `max-entries` | integer, 1 to 1000 | 100 | Pieces and outfits one character may keep |
| `short-length` | integer, 10 to 400 | 100 | The longest `+shortdesc` |
| `max-slots` | integer, 1 to 100 | 30 | Slots one player may have in their own list |
| `slots` | table | see below | The slots every player starts with |

The slots, on the Multi-descer Store as `` SLOT`<NAME> `` (the order) with `PARAGRAPH`, `COVERS` and `MAX`:

| Slot | Order | Paragraph | Covers | Max |
|---|---|---|---|---|
| `whole` | 10 | description | | none |
| `body` | 20 | body | | none |
| `face` | 30 | body | | none |
| `hair` | 40 | body | | 1 |
| `top` | 50 | clothes | | none |
| `outer` | 55 | clothes | top | 1 |
| `bottom` | 60 | clothes | | none |
| `feet` | 70 | clothes | | 1 |
| `accessory` | 90 | clothes | | none |

## Objects

- **Multi-descer**, in the master room, `WIZARD`: the commands and settings.
- **Multi-descer Store**, inside it: the slots.
- **Multi-descer Help**, inside it: the `+help desc`, `+help wear` and `+help shortdesc` topics. The
  `multidescer-help` bridge registers them with the `+help` librarian.

Requires `http-handler` 1.0 or later, which holds the routes the portal app reads, even on a game without `multidescer-app`.
