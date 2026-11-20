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
- **Slots.** Staff set them as a `+config` table: the order pieces come in, which slots share a paragraph,
  which slots a slot covers (a coat hides the shirt) and how many pieces may be worn in one at once.
- **Outfits.** `+desc/outfit <name>` saves the pieces worn now; `+desc <outfit>` wears them.
- **Nothing lost.** The first time a character uses it, the description they have is saved as `original`.
- **Evaluated once.** What is typed is evaluated as the player and kept as `decompose()` of the result, so
  `DESCRIBE`, which `look` evaluates, shows it as typed and never runs it again.
- **`+shortdesc <text>`** sets `SHORTDESC` on the character, a one-line look for other code to show.

Every change writes the pieces worn into the character's `DESCRIBE`, so `look`, `@descformat` and the portal
read it as they always do. Editing `DESCRIBE` by hand still works, and the next `+desc`, `+wear` or `+remove`
writes over it; `+desc/save <name>` keeps it first.

## Storage

On the character, under a root flagged `wizard` and `no_command` the first time the package writes it:

| Attribute | Holds |
|---|---|
| ``MULTIDESC`PIECE`<id>`NAME`` | The piece's name |
| ``MULTIDESC`PIECE`<id>`SLOT`` | Its slot |
| ``MULTIDESC`PIECE`<id>`TEXT`` | Its text, as code for `DESCRIBE` |
| ``MULTIDESC`OUTFIT`<id>`NAME`` | The outfit's name |
| ``MULTIDESC`OUTFIT`<id>`PIECES`` | Its piece ids |
| ``MULTIDESC`WORN`` | The piece ids worn now, in the order put on |
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
| `slots` | table | see below | Where pieces are worn |

The slots, on the Multi-descer Store as `` SLOT`<NAME> `` (the order) with `PARAGRAPH`, `COVERS` and `MAX`:

| Slot | Order | Paragraph | Covers | Most worn |
|---|---|---|---|---|
| `whole` | 10 | description | | any |
| `body` | 20 | body | | any |
| `face` | 30 | body | | any |
| `hair` | 40 | body | | 1 |
| `top` | 50 | clothes | | any |
| `outer` | 55 | clothes | top | 1 |
| `bottom` | 60 | clothes | | any |
| `feet` | 70 | clothes | | 1 |
| `accessory` | 90 | clothes | | any |

## Objects

- **Multi-descer**, in the master room, `WIZARD`: the commands and settings.
- **Multi-descer Store**, inside it: the slots.
- **Multi-descer Help**, inside it: the `+help desc`, `+help wear` and `+help shortdesc` topics. The
  `multidescer-help` bridge registers them with the `+help` librarian.

Requires nothing else.
